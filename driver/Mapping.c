#include "Driver.h"

///////////////////////////////////////////////////////////////////////////////
// Mapping engine
//
// Works like Apple KeyMagic2's brightness/volume handling, generalized: the
// physical keyboard only ever sends F1-F12 plus an Fn bit. Action-layer keys are
// removed from the keyboard's own report and their mapped usage is sent through
// a virtual HID device (VHF) instead. Windows handles consumer usages
// (brightness, volume, media, calculator), system-control usages (power button,
// sleep) and keyboard shortcuts natively, so no user-mode app is required.
//

// Virtual device: keyboard (ID 1), consumer control (ID 2), system control (ID 3)
static UCHAR g_Descriptor[] = {
	0x05, 0x01, 0x09, 0x06, 0xA1, 0x01, 0x85, 0x01,       // Generic Desktop Keyboard, report 1
	0x05, 0x07, 0x19, 0xE0, 0x29, 0xE7, 0x15, 0x00,       //  modifiers
	0x25, 0x01, 0x75, 0x01, 0x95, 0x08, 0x81, 0x02,
	0x95, 0x01, 0x75, 0x08, 0x81, 0x01,                   //  reserved byte
	0x19, 0x00, 0x2A, 0xFF, 0x00, 0x15, 0x00, 0x26,       //  6 key array
	0xFF, 0x00, 0x75, 0x08, 0x95, 0x06, 0x81, 0x00,
	0xC0,
	0x05, 0x0C, 0x09, 0x01, 0xA1, 0x01, 0x85, 0x02,       // Consumer Control, report 2
	0x19, 0x00, 0x2A, 0xFF, 0x03, 0x15, 0x00, 0x26,       //  2 x 16-bit usage array
	0xFF, 0x03, 0x75, 0x10, 0x95, 0x02, 0x81, 0x00,
	0xC0,
	0x05, 0x01, 0x09, 0x80, 0xA1, 0x01, 0x85, 0x03,       // System Control, report 3
	0x19, 0x81, 0x29, 0x83, 0x15, 0x00, 0x25, 0x01,       //  power down, sleep, wake
	0x75, 0x01, 0x95, 0x03, 0x81, 0x02,
	0x95, 0x05, 0x81, 0x01,
	0xC0
};

typedef struct tagGESTURE
{
	BOOLEAN Down, Cancelled, Waiting, Second, Fired;
	ULONG Pressed, Released;
} GESTURE;

static PDRIVER_OBJECT g_Driver;
static KSPIN_LOCK g_EngineLock; // all state below
static MAGICKB_CONFIG g_Config;
static VHFHANDLE g_Vhf;
static KTIMER g_Timer;
static KDPC g_TimerDpc;
static BOOLEAN g_TimerRunning;
static GESTURE g_Gesture[2];
static BOOLEAN g_FnDown, g_LockDown;
static USHORT g_PrevRow, g_ActionRow; // F1-F12 bit masks
static MAGICKB_ACTION g_Held[12]; // action started by each action-layer row key
static UCHAR g_KeyMods, g_Keys[6]; // virtual keyboard state
static USHORT g_Consumer[2];
static UCHAR g_System;

static ULONG NowMs(void)
{
	return (ULONG)(KeQueryInterruptTime() / 10000);
}

static void SetAction(MAGICKB_ACTION* action, UCHAR type, UCHAR modifiers, USHORT usage)
{
	action->Type = type;
	action->Modifiers = modifiers;
	action->Usage = usage;
}

// Defaults follow the Mac function row: brightness, Task View, Search, voice typing,
// screenshot (Windows has no Do Not Disturb key), media, mute and volume. Fn tap
// switches input language; holding Lock for one second locks Windows.
static void DefaultConfig(MAGICKB_CONFIG* c)
{
	static const USHORT consumer[12] = { 0x70, 0x6F, 0, 0, 0, 0, 0xB6, 0xCD, 0xB5, 0xE2, 0xEA, 0xE9 };
	static const UCHAR chord[12] = { 0, 0, 0x2B, 0x16, 0x0B, 0x16 }; // Win + Tab, S, H; Win+Shift+S
	RtlZeroMemory(c, sizeof(*c));
	c->Version = MAGICKB_CONFIG_VERSION;
	c->DoubleTapMs = 300;
	c->LongPressMs = 650;
	c->LockHoldMs = 1000;
	for (int i = 0; i < 12; ++i)
	{
		if (consumer[i])
			SetAction(&c->Row[i], MAGICKB_ACTION_CONSUMER, 0, consumer[i]);
		else
			SetAction(&c->Row[i], MAGICKB_ACTION_CHORD, 0x08, chord[i]);
	}
	c->Row[5].Modifiers = 0x0A;
	// Search and voice typing toggle through the tray app; without it the driver taps
	// Win+S / Win+H.
	c->Row[3].Type = MAGICKB_ACTION_APP;
	c->Row[4].Type = MAGICKB_ACTION_APP;
	SetAction(&c->Gesture[MAGICKB_GESTURE_FN][0], MAGICKB_ACTION_CHORD, 0x08, 0x2C); // Win+Space
	SetAction(&c->Gesture[MAGICKB_GESTURE_LOCK][2], MAGICKB_ACTION_CHORD, 0x08, 0x0F); // Win+L
	for (UCHAR i = 0; i < 8; ++i)
		c->ModifierMap[i] = i; // Control, Option (Alt) and Command (Win) as labeled
}

static BOOLEAN ValidAction(const MAGICKB_ACTION* a, BOOLEAN gesture)
{
	switch (a->Type)
	{
	case MAGICKB_ACTION_NONE:
		return TRUE;
	case MAGICKB_ACTION_APP:
		return a->Usage <= 0xFF;
	case MAGICKB_ACTION_STANDARD:
		return !gesture;
	case MAGICKB_ACTION_KEY:
	case MAGICKB_ACTION_CHORD:
		return a->Usage <= 0xFF && (a->Usage || a->Modifiers);
	case MAGICKB_ACTION_CONSUMER:
		return a->Usage && a->Usage <= 0x3FF;
	case MAGICKB_ACTION_SYSTEM:
		return a->Usage >= 0x81 && a->Usage <= 0x83;
	}
	return FALSE;
}

static BOOLEAN ValidConfig(const MAGICKB_CONFIG* c)
{
	if (c->Version != MAGICKB_CONFIG_VERSION || (c->Flags & ~MAGICKB_FLAG_ACTIONS_FIRST))
		return FALSE;
	if (c->DoubleTapMs < 100 || c->DoubleTapMs > 1000 || c->LongPressMs < 200 || c->LongPressMs > 5000 ||
		c->LockHoldMs < 500 || c->LockHoldMs > 5000)
		return FALSE;
	for (int i = 0; i < 8; ++i)
		if (c->ModifierMap[i] > 7)
			return FALSE;
	// Fn itself has only a tap action.
	if (c->Gesture[MAGICKB_GESTURE_FN][1].Type != MAGICKB_ACTION_NONE ||
		c->Gesture[MAGICKB_GESTURE_FN][2].Type != MAGICKB_ACTION_NONE)
		return FALSE;
	for (int i = 0; i < 12; ++i)
		if (!ValidAction(&c->Row[i], FALSE))
			return FALSE;
	for (int g = 0; g < 2; ++g)
		for (int i = 0; i < 3; ++i)
			if (!ValidAction(&c->Gesture[g][i], TRUE))
				return FALSE;
	return TRUE;
}

// The profile lives in the driver's persistent-state key: Windows makes the service's
// Parameters key read-only to the driver, so writes there fail with access denied.
static NTSTATUS OpenState(PHANDLE key, ACCESS_MASK access)
{
	return IoOpenDriverRegistryKey(g_Driver, DriverRegKeyPersistentState, access, 0, key);
}

static BOOLEAN LoadConfig(MAGICKB_CONFIG* config)
{
	UNICODE_STRING name = RTL_CONSTANT_STRING(L"Config");
	UCHAR buffer[sizeof(KEY_VALUE_PARTIAL_INFORMATION) + sizeof(MAGICKB_CONFIG)];
	PKEY_VALUE_PARTIAL_INFORMATION value = (PKEY_VALUE_PARTIAL_INFORMATION)buffer;
	HANDLE key;
	ULONG length;
	if (!NT_SUCCESS(OpenState(&key, KEY_READ)))
		return FALSE;
	NTSTATUS status = ZwQueryValueKey(key, &name, KeyValuePartialInformation, buffer, sizeof(buffer), &length);
	ZwClose(key);
	if (!NT_SUCCESS(status) || value->Type != REG_BINARY || value->DataLength != sizeof(MAGICKB_CONFIG))
		return FALSE;
	RtlCopyMemory(config, value->Data, sizeof(MAGICKB_CONFIG));
	return ValidConfig(config);
}

static NTSTATUS SaveConfig(const MAGICKB_CONFIG* config)
{
	UNICODE_STRING name = RTL_CONSTANT_STRING(L"Config");
	HANDLE key;
	NTSTATUS status = OpenState(&key, KEY_WRITE);
	if (!NT_SUCCESS(status))
		return status;
	status = ZwSetValueKey(key, &name, 0, REG_BINARY, (PVOID)config, sizeof(*config));
	ZwClose(key);
	return status;
}

///////////////////////////////////////////////////////////////////////////////
// Virtual HID output (called with g_EngineLock held)

static void Submit(UCHAR id, UCHAR* report, ULONG length)
{
	if (!g_Vhf)
		return;
	report[0] = id;
	HID_XFER_PACKET packet;
	packet.reportBuffer = report;
	packet.reportBufferLen = length;
	packet.reportId = id;
	NTSTATUS status = VhfReadReportSubmit(g_Vhf, &packet);
	if (!NT_SUCCESS(status))
		DebugPrint("VhfReadReportSubmit(%u) failed: 0x%x", id, status);
}

static void SubmitKeyboard(UCHAR extraModifiers, UCHAR extraUsage)
{
	UCHAR report[9] = { 0 };
	report[1] = g_KeyMods | extraModifiers;
	RtlCopyMemory(&report[3], g_Keys, sizeof(g_Keys));
	for (int i = 3; extraUsage && i < 9; ++i)
		if (!report[i])
		{
			report[i] = extraUsage;
			break;
		}
	Submit(1, report, sizeof(report));
}

static void SubmitConsumer(void)
{
	UCHAR report[5];
	report[1] = (UCHAR)g_Consumer[0];
	report[2] = (UCHAR)(g_Consumer[0] >> 8);
	report[3] = (UCHAR)g_Consumer[1];
	report[4] = (UCHAR)(g_Consumer[1] >> 8);
	Submit(2, report, sizeof(report));
}

static void SubmitSystem(void)
{
	UCHAR report[2];
	report[1] = g_System;
	Submit(3, report, sizeof(report));
}

static void ActionDown(const MAGICKB_ACTION* a)
{
	switch (a->Type)
	{
	case MAGICKB_ACTION_KEY:
		g_KeyMods |= a->Modifiers;
		for (int i = 0; a->Usage && i < 6; ++i)
			if (!g_Keys[i] || g_Keys[i] == a->Usage)
			{
				g_Keys[i] = (UCHAR)a->Usage;
				break;
			}
		SubmitKeyboard(0, 0);
		break;
	case MAGICKB_ACTION_CHORD:
		SubmitKeyboard(a->Modifiers, (UCHAR)a->Usage);
		SubmitKeyboard(0, 0);
		break;
	case MAGICKB_ACTION_CONSUMER:
		for (int i = 0; i < 2; ++i)
			if (!g_Consumer[i] || g_Consumer[i] == a->Usage)
			{
				g_Consumer[i] = a->Usage;
				break;
			}
		SubmitConsumer();
		break;
	case MAGICKB_ACTION_SYSTEM:
		g_System |= (UCHAR)(1 << (a->Usage - 0x81));
		SubmitSystem();
		break;
	}
}

static void ActionUp(const MAGICKB_ACTION* a)
{
	switch (a->Type)
	{
	case MAGICKB_ACTION_KEY:
		g_KeyMods &= ~a->Modifiers;
		for (int i = 0; i < 6; ++i)
			if (g_Keys[i] == a->Usage)
				g_Keys[i] = 0;
		SubmitKeyboard(0, 0);
		break;
	case MAGICKB_ACTION_CONSUMER:
		for (int i = 0; i < 2; ++i)
			if (g_Consumer[i] == a->Usage)
				g_Consumer[i] = 0;
		SubmitConsumer();
		break;
	case MAGICKB_ACTION_SYSTEM:
		g_System &= ~(UCHAR)(1 << (a->Usage - 0x81));
		SubmitSystem();
		break;
	}
}

// Hands an app action to the tray app, or taps its fallback shortcut when no tray is reading.
static void RunApp(UCHAR key, UCHAR press, const MAGICKB_ACTION* a)
{
	if (ReaderConnected())
		PublishEvent(MAGICKB_EVENT_APP, key, press, NULL);
	else if (a->Usage || a->Modifiers)
	{
		SubmitKeyboard(a->Modifiers, (UCHAR)a->Usage);
		SubmitKeyboard(0, 0);
	}
}

static void RunGestureAction(int gesture, int press)
{
	const MAGICKB_ACTION* a = &g_Config.Gesture[gesture][press];
	if (a->Type == MAGICKB_ACTION_APP)
	{
		RunApp(gesture == MAGICKB_GESTURE_LOCK ? 12 : 13, (UCHAR)press, a);
		return;
	}
	ActionDown(a);
	ActionUp(a);
}

// Single/double/long press tracker, same rules as PressGesture in gui\PhysicalInput.cs.
static void UpdateGesture(int g, BOOLEAN active, BOOLEAN chord, ULONG now)
{
	GESTURE* s = &g_Gesture[g];
	BOOLEAN hasDouble = g_Config.Gesture[g][1].Type != MAGICKB_ACTION_NONE;
	BOOLEAN hasLong = g_Config.Gesture[g][2].Type != MAGICKB_ACTION_NONE;
	BOOLEAN whileHeld = g == MAGICKB_GESTURE_LOCK;
	ULONG longMs = whileHeld ? g_Config.LockHoldMs : g_Config.LongPressMs;

	if (chord && (active || s->Down))
	{
		s->Cancelled = TRUE;
		s->Waiting = s->Second = FALSE;
	}
	if (s->Waiting && !s->Second && now - s->Released >= g_Config.DoubleTapMs)
	{
		s->Waiting = FALSE;
		RunGestureAction(g, 0);
	}
	if (active && !s->Down)
	{
		s->Down = TRUE;
		s->Pressed = now;
		s->Fired = FALSE;
		s->Cancelled = chord;
		s->Second = s->Waiting;
	}
	if (s->Down && !s->Cancelled && !s->Fired && hasLong && (whileHeld || !active) && now - s->Pressed >= longMs)
	{
		s->Waiting = s->Second = FALSE;
		s->Fired = TRUE;
		RunGestureAction(g, 2);
	}
	if (active || !s->Down)
		return;
	s->Down = FALSE;
	if (s->Cancelled || s->Fired)
		return;
	if (s->Second)
	{
		s->Waiting = s->Second = FALSE;
		RunGestureAction(g, 1);
	}
	else if (hasDouble)
	{
		s->Waiting = TRUE;
		s->Released = now;
	}
	else
		RunGestureAction(g, 0);
}

// Polls gestures every 20 ms only while a press is held or a double tap may follow.
static void ArmTimer(void)
{
	BOOLEAN needed = g_Gesture[0].Down || g_Gesture[0].Waiting || g_Gesture[1].Down || g_Gesture[1].Waiting;
	if (needed && !g_TimerRunning)
	{
		LARGE_INTEGER due;
		due.QuadPart = -20 * 10000LL;
		KeSetTimerEx(&g_Timer, due, 20, &g_TimerDpc);
		g_TimerRunning = TRUE;
	}
	else if (!needed && g_TimerRunning)
	{
		KeCancelTimer(&g_Timer);
		g_TimerRunning = FALSE;
	}
}

static VOID TimerDpc(PKDPC dpc, PVOID context, PVOID arg1, PVOID arg2)
{
	UNREFERENCED_PARAMETER(dpc);
	UNREFERENCED_PARAMETER(context);
	UNREFERENCED_PARAMETER(arg1);
	UNREFERENCED_PARAMETER(arg2);
	KeAcquireSpinLockAtDpcLevel(&g_EngineLock);
	ULONG now = NowMs();
	UpdateGesture(MAGICKB_GESTURE_LOCK, g_LockDown, FALSE, now);
	UpdateGesture(MAGICKB_GESTURE_FN, g_FnDown, FALSE, now);
	ArmTimer();
	KeReleaseSpinLockFromDpcLevel(&g_EngineLock);
}

// Releases everything the virtual device holds and forgets in-progress presses.
static void ResetState(void)
{
	for (int i = 0; i < 12; ++i)
		if (g_ActionRow & (1 << i))
			ActionUp(&g_Held[i]);
	g_ActionRow = 0;
	RtlZeroMemory(g_Gesture, sizeof(g_Gesture));
	g_KeyMods = 0;
	RtlZeroMemory(g_Keys, sizeof(g_Keys));
	RtlZeroMemory(g_Consumer, sizeof(g_Consumer));
	g_System = 0;
	ArmTimer();
}

///////////////////////////////////////////////////////////////////////////////
// Public entry points

void EngineInitialize(PDRIVER_OBJECT driverObject)
{
	g_Driver = driverObject;
	KeInitializeSpinLock(&g_EngineLock);
	KeInitializeTimerEx(&g_Timer, NotificationTimer);
	KeInitializeDpc(&g_TimerDpc, TimerDpc, NULL);
	if (!LoadConfig(&g_Config))
		DefaultConfig(&g_Config);
}

NTSTATUS EngineCreateVhf(PDEVICE_OBJECT fido, VHFHANDLE* vhf)
{
	VHF_CONFIG config;
	VHF_CONFIG_INIT(&config, fido, sizeof(g_Descriptor), g_Descriptor);
	NTSTATUS status = VhfCreate(&config, vhf);
	if (!NT_SUCCESS(status))
	{
		*vhf = NULL;
		return status;
	}
	status = VhfStart(*vhf);
	if (!NT_SUCCESS(status))
	{
		VhfDelete(*vhf, TRUE);
		*vhf = NULL;
	}
	return status;
}

void EngineStart(VHFHANDLE vhf)
{
	KIRQL irql;
	KeAcquireSpinLock(&g_EngineLock, &irql);
	g_Vhf = vhf;
	g_PrevRow = 0;
	ResetState();
	KeReleaseSpinLock(&g_EngineLock, irql);
}

// Must be called at PASSIVE_LEVEL; after it returns no timer DPC uses the VHF handle.
void EngineStop(void)
{
	KIRQL irql;
	KeAcquireSpinLock(&g_EngineLock, &irql);
	ResetState();
	g_Vhf = NULL;
	KeCancelTimer(&g_Timer);
	g_TimerRunning = FALSE;
	KeReleaseSpinLock(&g_EngineLock, irql);
	KeFlushQueuedDpcs();
}

void EngineGetConfig(MAGICKB_CONFIG* config)
{
	KIRQL irql;
	KeAcquireSpinLock(&g_EngineLock, &irql);
	*config = g_Config;
	KeReleaseSpinLock(&g_EngineLock, irql);
}

NTSTATUS EngineSetConfig(const MAGICKB_CONFIG* config)
{
	if (!ValidConfig(config))
		return STATUS_INVALID_PARAMETER;
	KIRQL irql;
	KeAcquireSpinLock(&g_EngineLock, &irql);
	ResetState();
	g_Config = *config;
	KeReleaseSpinLock(&g_EngineLock, irql);
	return SaveConfig(config);
}

// Called for every keyboard input report (ID 1) from the device, in place.
void EngineProcessReport(UCHAR* report)
{
	UCHAR raw[MAGICKB_REPORT_LENGTH];
	RtlCopyMemory(raw, report, sizeof(raw));
	// Remap Control / Option / Command first, so Windows and the action layer both see
	// the remapped modifiers.
	KIRQL irql;
	UCHAR map[8];
	KeAcquireSpinLock(&g_EngineLock, &irql);
	RtlCopyMemory(map, g_Config.ModifierMap, sizeof(map));
	KeReleaseSpinLock(&g_EngineLock, irql);
	UCHAR mods = 0;
	for (int i = 0; i < 8; ++i)
		if (report[1] & (1 << i))
			mods |= (UCHAR)(1 << (map[i] & 7));
	report[1] = mods;
	BOOLEAN fn = (report[9] & 0x02) != 0;
	BOOLEAN lock = (report[9] & 0x08) != 0;
	BOOLEAN other = report[1] != 0 || (report[9] & 0x05) != 0;
	USHORT row = 0;
	for (int i = 3; i < 9; ++i)
	{
		UCHAR usage = report[i];
		if (usage >= 0x3A && usage <= 0x45)
			row |= (USHORT)(1 << (usage - 0x3A));
		else if (usage)
			other = TRUE;
	}

	KeAcquireSpinLock(&g_EngineLock, &irql);
	if (!g_Vhf)
	{
		// No virtual device to carry actions: leave the report untouched.
		KeReleaseSpinLock(&g_EngineLock, irql);
		PublishEvent(MAGICKB_EVENT_REPORT, 0, 0, raw);
		return;
	}
	BOOLEAN actionLayer = (g_Config.Flags & MAGICKB_FLAG_ACTIONS_FIRST) ? !fn : fn;
	USHORT pressed = row & ~g_PrevRow, released = g_PrevRow & ~row;
	for (int i = 0; i < 12; ++i)
	{
		USHORT bit = (USHORT)(1 << i);
		if ((released & bit) && (g_ActionRow & bit))
		{
			ActionUp(&g_Held[i]);
			g_ActionRow &= ~bit;
		}
	}
	for (int i = 0; i < 12; ++i)
	{
		USHORT bit = (USHORT)(1 << i);
		if (!(pressed & bit) || !actionLayer || g_Config.Row[i].Type == MAGICKB_ACTION_STANDARD)
			continue;
		g_Held[i] = g_Config.Row[i];
		g_ActionRow |= bit;
		if (g_Held[i].Type == MAGICKB_ACTION_APP)
			RunApp((UCHAR)i, 0, &g_Held[i]);
		else
			ActionDown(&g_Held[i]); // a chord taps here; ActionUp ignores it
	}

	// Remove action-layer keys from the keyboard's own report and hide Lock
	// (AL Terminal Lock) from Windows; the engine owns both.
	UCHAR keys[6] = { 0 };
	int count = 0;
	for (int i = 3; i < 9; ++i)
	{
		UCHAR usage = report[i];
		if (usage >= 0x3A && usage <= 0x45 && (g_ActionRow & (1 << (usage - 0x3A))))
			continue;
		if (usage)
			keys[count++] = usage;
	}
	RtlCopyMemory(&report[3], keys, sizeof(keys));
	report[9] &= ~0x08;

	ULONG now = NowMs();
	g_PrevRow = row;
	g_FnDown = fn;
	g_LockDown = lock;
	UpdateGesture(MAGICKB_GESTURE_LOCK, lock, FALSE, now);
	UpdateGesture(MAGICKB_GESTURE_FN, fn, other || row != 0 || lock, now);
	ArmTimer();
	KeReleaseSpinLock(&g_EngineLock, irql);

	PublishEvent(MAGICKB_EVENT_REPORT, 0, 0, raw);
}
