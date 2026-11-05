#include "Driver.h"
#include <initguid.h> // instantiates GUID_MAGICKB_CONTROL below, in this file only
DRIVER_ADD_DEVICE AddDevice;
static UCHAR GetLockMask(PDEVICE_OBJECT pdo);

#ifdef ALLOC_PRAGMA
	#pragma alloc_text (INIT, DriverEntry)
	#pragma alloc_text (PAGE, DriverUnload)
	#pragma alloc_text (PAGE, AddDevice)
	#pragma alloc_text (PAGE, GetLockMask)
	#pragma alloc_text (PAGE, RemoveDevice)
	#pragma alloc_text (PAGE, GetLowerDeviceType)
#endif // ALLOC_PRAGMA

// {2C5B0E4A-6F0B-4B2E-9C61-0D7E2F1A0320}
DEFINE_GUID(GUID_MAGICKB_CONTROL, 0x2c5b0e4a, 0x6f0b, 0x4b2e, 0x9c, 0x61, 0x0d, 0x7e, 0x2f, 0x1a, 0x03, 0x20);

// The battery level Windows shows in Settings > Bluetooth & devices (DEVPKEY_Bluetooth_Battery,
// a BYTE percentage). The Bluetooth stack sets it only for hands-free and LE battery devices;
// for this keyboard the driver sets it from the keyboard's own battery report.
static const DEVPROPKEY DEVPKEY_OrchardBluetoothBattery = { { 0x104ea319, 0x6ee2, 0x4701, { 0xbd, 0x47, 0x8d, 0xdb, 0xf4, 0x25, 0xbb, 0xe5 } }, 2 };

///////////////////////////////////////////////////////////////////////////////
// Control device and report ring
//
// The ring carries physical reports (for live display) and app-action events to
// the tray app, which drains it through \\.\MagicKeyboardFn. The control object
// exists while at least one filtered keyboard is present.
//
static PDEVICE_OBJECT g_Control;
static ULONG g_FilterCount;
static KEVENT g_ControlGate; // synchronization event used as a PASSIVE_LEVEL mutex
static KSPIN_LOCK g_RingLock; // ring state; may be held while taking g_QueueLock
static KSPIN_LOCK g_QueueLock; // pending reads, owned by the cancel-safe queue
static IO_CSQ g_Queue;
static LIST_ENTRY g_Pending;
static MAGICKB_EVENT g_Ring[MAGICKB_RING_LENGTH];
static ULONG g_Head, g_Count, g_Sequence;
static PFILE_OBJECT volatile g_Reader; // handle the tray app reads events on, if any (ring lock)
static ULONG g_ReaderSession; // session of that handle's process

// A reader counts only while its session is the one at the console: after a user switch,
// app actions fall back to the driver's own shortcuts until the new user's tray connects.
BOOLEAN ReaderConnected(void)
{
	return g_Reader != NULL && g_ReaderSession == SharedUserData->ActiveConsoleId;
}

static BOOLEAN IsControl(PDEVICE_OBJECT device)
{
	return ((PDEVICE_EXTENSION)device->DeviceExtension)->IsControl;
}

static VOID CsqInsert(PIO_CSQ csq, PIRP irp)
{
	UNREFERENCED_PARAMETER(csq);
	InsertTailList(&g_Pending, &irp->Tail.Overlay.ListEntry);
}

static VOID CsqRemove(PIO_CSQ csq, PIRP irp)
{
	UNREFERENCED_PARAMETER(csq);
	RemoveEntryList(&irp->Tail.Overlay.ListEntry);
}

// A non-NULL context limits the search to reads from that file object.
static PIRP CsqPeek(PIO_CSQ csq, PIRP irp, PVOID context)
{
	UNREFERENCED_PARAMETER(csq);
	PLIST_ENTRY entry = irp ? irp->Tail.Overlay.ListEntry.Flink : g_Pending.Flink;
	for (; entry != &g_Pending; entry = entry->Flink)
	{
		PIRP next = CONTAINING_RECORD(entry, IRP, Tail.Overlay.ListEntry);
		if (!context || IoGetCurrentIrpStackLocation(next)->FileObject == (PFILE_OBJECT)context)
			return next;
	}
	return NULL;
}

_IRQL_raises_(DISPATCH_LEVEL)
static VOID CsqAcquire(PIO_CSQ csq, PKIRQL irql)
{
	UNREFERENCED_PARAMETER(csq);
	KeAcquireSpinLock(&g_QueueLock, irql);
}

_IRQL_requires_(DISPATCH_LEVEL)
static VOID CsqRelease(PIO_CSQ csq, KIRQL irql)
{
	UNREFERENCED_PARAMETER(csq);
	KeReleaseSpinLock(&g_QueueLock, irql);
}

static VOID CsqComplete(PIO_CSQ csq, PIRP irp)
{
	UNREFERENCED_PARAMETER(csq);
	CompleteRequest(irp, STATUS_CANCELLED, 0);
}

static void CompleteRead(PIRP irp, const MAGICKB_EVENT* report)
{
	RtlCopyMemory(irp->AssociatedIrp.SystemBuffer, report, sizeof(*report));
	CompleteRequest(irp, STATUS_SUCCESS, sizeof(*report));
}

// Pops a report and a waiting read together under g_RingLock, so a report
// is never left in the ring while a read is pending.
static void DeliverReports(void)
{
	for (;;)
	{
		MAGICKB_EVENT report;
		KIRQL irql;
		KeAcquireSpinLock(&g_RingLock, &irql);
		PIRP irp = g_Count ? IoCsqRemoveNextIrp(&g_Queue, NULL) : NULL;
		if (irp)
		{
			report = g_Ring[g_Head];
			g_Head = (g_Head + 1) % MAGICKB_RING_LENGTH;
			--g_Count;
		}
		KeReleaseSpinLock(&g_RingLock, irql);
		if (!irp)
			return;
		CompleteRead(irp, &report);
	}
}

void PublishEvent(UCHAR kind, UCHAR key, UCHAR gesture, const UCHAR* report)
{
	KIRQL irql;
	KeAcquireSpinLock(&g_RingLock, &irql);
	if (g_Count == MAGICKB_RING_LENGTH)
	{
		g_Head = (g_Head + 1) % MAGICKB_RING_LENGTH;
		--g_Count;
	}
	MAGICKB_EVENT* slot = &g_Ring[(g_Head + g_Count) % MAGICKB_RING_LENGTH];
	RtlZeroMemory(slot, sizeof(*slot));
	slot->Sequence = ++g_Sequence;
	slot->Kind = kind;
	slot->Key = key;
	slot->Gesture = gesture;
	if (report)
		RtlCopyMemory(slot->Report, report, MAGICKB_REPORT_LENGTH);
	++g_Count;
	KeReleaseSpinLock(&g_RingLock, irql);
	DeliverReports();
}

static void FailPendingReads(PFILE_OBJECT file, NTSTATUS status)
{
	PIRP irp;
	while ((irp = IoCsqRemoveNextIrp(&g_Queue, file)) != NULL)
		CompleteRequest(irp, status, 0);
}

static void CreateControl(PDRIVER_OBJECT driverObject)
{
	UNICODE_STRING name = RTL_CONSTANT_STRING(L"\\Device\\MagicKeyboardFn");
	UNICODE_STRING link = RTL_CONSTANT_STRING(L"\\DosDevices\\MagicKeyboardFn");
	UNICODE_STRING sddl = RTL_CONSTANT_STRING(L"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGW;;;IU)");
	PDEVICE_OBJECT control;
	NTSTATUS status = IoCreateDeviceSecure(driverObject, sizeof(DEVICE_EXTENSION), &name, FILE_DEVICE_UNKNOWN,
		FILE_DEVICE_SECURE_OPEN, FALSE, &sddl, &GUID_MAGICKB_CONTROL, &control);
	if (!NT_SUCCESS(status))
	{
		DebugPrint("CreateControl(): IoCreateDeviceSecure failed: 0x%x", status);
		return;
	}
	RtlZeroMemory(control->DeviceExtension, sizeof(DEVICE_EXTENSION));
	((PDEVICE_EXTENSION)control->DeviceExtension)->IsControl = TRUE;
	status = IoCreateSymbolicLink(&link, &name);
	if (!NT_SUCCESS(status))
	{
		DebugPrint("CreateControl(): IoCreateSymbolicLink failed: 0x%x", status);
		IoDeleteDevice(control);
		return;
	}
	control->Flags |= DO_BUFFERED_IO;
	control->Flags &= ~DO_DEVICE_INITIALIZING;
	g_Control = control;
}

// The previous control object keeps its name until the tray app closes its handle, so
// creating the new one right after a keyboard restart can collide. Retry for ~10 s.
static VOID RetryControl(PDEVICE_OBJECT fido, PVOID context)
{
	LARGE_INTEGER delay;
	delay.QuadPart = -200 * 10000LL;
	for (int i = 0; i < 50; ++i)
	{
		KeDelayExecutionThread(KernelMode, FALSE, &delay);
		KeWaitForSingleObject(&g_ControlGate, Executive, KernelMode, FALSE, NULL);
		if (!g_Control && g_FilterCount)
			CreateControl(fido->DriverObject);
		BOOLEAN done = g_Control != NULL || g_FilterCount == 0;
		KeSetEvent(&g_ControlGate, IO_NO_INCREMENT, FALSE);
		if (done)
			break;
	}
	IoFreeWorkItem((PIO_WORKITEM)context);
}

static void DeleteControl(void)
{
	UNICODE_STRING link = RTL_CONSTANT_STRING(L"\\DosDevices\\MagicKeyboardFn");
	if (!g_Control)
		return;
	// Handles still open on this object get STATUS_DEVICE_REMOVED from now on, so
	// clients reopen the next control object instead of reading through a dead one.
	((PDEVICE_EXTENSION)g_Control->DeviceExtension)->Deleted = TRUE;
	g_Reader = NULL;
	FailPendingReads(NULL, STATUS_DEVICE_NOT_CONNECTED);
	IoDeleteSymbolicLink(&link);
	IoDeleteDevice(g_Control);
	g_Control = NULL;
}

// Only the session at the physical console may use the control device: it carries
// function-row presses and sets the machine-wide mapping, so a user in another
// (switched-away or remote) session must not read or change them.
static BOOLEAN FromConsole(PIRP irp)
{
	ULONG session;
	return NT_SUCCESS(IoGetRequestorSessionId(irp, &session)) && session == SharedUserData->ActiveConsoleId;
}

NTSTATUS DispatchControl(IN PDEVICE_OBJECT control, IN PIRP irp)
{
	PIO_STACK_LOCATION stack = IoGetCurrentIrpStackLocation(irp);
	KIRQL irql;

	if (((PDEVICE_EXTENSION)control->DeviceExtension)->Deleted &&
		stack->MajorFunction != IRP_MJ_CLEANUP && stack->MajorFunction != IRP_MJ_CLOSE)
		return CompleteRequest(irp, STATUS_DEVICE_REMOVED, 0);

	switch (stack->MajorFunction)
	{
	case IRP_MJ_CREATE:
		return CompleteRequest(irp, FromConsole(irp) ? STATUS_SUCCESS : STATUS_ACCESS_DENIED, 0);

	case IRP_MJ_CLOSE:
		return CompleteRequest(irp, STATUS_SUCCESS, 0);

	case IRP_MJ_CLEANUP:
		KeAcquireSpinLock(&g_RingLock, &irql);
		if (g_Reader == stack->FileObject)
			g_Reader = NULL;
		KeReleaseSpinLock(&g_RingLock, irql);
		FailPendingReads(stack->FileObject, STATUS_CANCELLED);
		return CompleteRequest(irp, STATUS_SUCCESS, 0);

	case IRP_MJ_DEVICE_CONTROL:
		switch (stack->Parameters.DeviceIoControl.IoControlCode)
		{
		case IOCTL_MAGICKB_GET_CONFIG:
			if (stack->Parameters.DeviceIoControl.OutputBufferLength < sizeof(MAGICKB_CONFIG))
				return CompleteRequest(irp, STATUS_BUFFER_TOO_SMALL, 0);
			if (!FromConsole(irp))
				return CompleteRequest(irp, STATUS_ACCESS_DENIED, 0);
			EngineGetConfig((MAGICKB_CONFIG*)irp->AssociatedIrp.SystemBuffer);
			return CompleteRequest(irp, STATUS_SUCCESS, sizeof(MAGICKB_CONFIG));
		case IOCTL_MAGICKB_SET_CONFIG:
			if (stack->Parameters.DeviceIoControl.InputBufferLength != sizeof(MAGICKB_CONFIG))
				return CompleteRequest(irp, STATUS_INVALID_BUFFER_SIZE, 0);
			if (!FromConsole(irp))
				return CompleteRequest(irp, STATUS_ACCESS_DENIED, 0);
			return CompleteRequest(irp, EngineSetConfig((const MAGICKB_CONFIG*)irp->AssociatedIrp.SystemBuffer), 0);
		case IOCTL_MAGICKB_READ_EVENT:
			if (stack->Parameters.DeviceIoControl.OutputBufferLength < sizeof(MAGICKB_EVENT))
				return CompleteRequest(irp, STATUS_BUFFER_TOO_SMALL, 0);
			if (!FromConsole(irp))
				return CompleteRequest(irp, STATUS_ACCESS_DENIED, 0);
			if (stack->FileObject->Flags & FO_CLEANUP_COMPLETE)
				return CompleteRequest(irp, STATUS_CANCELLED, 0); // handle is closing
			{
				// One reader at a time, from the console session. A reader whose session
				// left the console (user switch) is replaced; a new reader starts from the
				// next event, not from stale history.
				ULONG session = SharedUserData->ActiveConsoleId;
				PFILE_OBJECT replaced = NULL;
				BOOLEAN busy = FALSE;
				KeAcquireSpinLock(&g_RingLock, &irql);
				if (g_Reader != stack->FileObject)
				{
					if (g_Reader && g_ReaderSession == session)
						busy = TRUE;
					else
					{
						replaced = g_Reader;
						g_Reader = stack->FileObject;
						g_ReaderSession = session;
						g_Head = g_Count = 0;
					}
				}
				KeReleaseSpinLock(&g_RingLock, irql);
				if (busy)
					return CompleteRequest(irp, STATUS_SHARING_VIOLATION, 0);
				if (replaced)
					FailPendingReads(replaced, STATUS_CANCELLED);
			}
			break;
		default:
			return CompleteRequest(irp, STATUS_INVALID_DEVICE_REQUEST, 0);
		}
		{
			MAGICKB_EVENT report;
			BOOLEAN ready;
			KeAcquireSpinLock(&g_RingLock, &irql);
			ready = g_Count != 0;
			if (ready)
			{
				report = g_Ring[g_Head];
				g_Head = (g_Head + 1) % MAGICKB_RING_LENGTH;
				--g_Count;
			}
			else
			{
				IoCsqInsertIrp(&g_Queue, irp, NULL);
			}
			KeReleaseSpinLock(&g_RingLock, irql);
			if (!ready)
				return STATUS_PENDING;
			CompleteRead(irp, &report);
			return STATUS_SUCCESS;
		}

	case IRP_MJ_POWER:
		PoStartNextPowerIrp(irp);
		break;
	}

	return CompleteRequest(irp, STATUS_INVALID_DEVICE_REQUEST, 0);
}

///////////////////////////////////////////////////////////////////////////////
// Driver Entry
//
NTSTATUS DriverEntry(IN PDRIVER_OBJECT driverObject, IN PUNICODE_STRING registryPath)
{
	UNREFERENCED_PARAMETER(registryPath);

	KeInitializeEvent(&g_ControlGate, SynchronizationEvent, TRUE);
	KeInitializeSpinLock(&g_RingLock);
	KeInitializeSpinLock(&g_QueueLock);
	InitializeListHead(&g_Pending);
	NTSTATUS status = IoCsqInitialize(&g_Queue, CsqInsert, CsqRemove, CsqPeek, CsqAcquire, CsqRelease, CsqComplete);
	if (!NT_SUCCESS(status))
		return status;
	EngineInitialize(driverObject);

	driverObject->DriverUnload = DriverUnload;
	driverObject->DriverExtension->AddDevice = AddDevice;

	for (int i = 0; i <= IRP_MJ_MAXIMUM_FUNCTION; i++)
		driverObject->MajorFunction[i] = DispatchAny;

	driverObject->MajorFunction[IRP_MJ_POWER] = DispatchPower;
	driverObject->MajorFunction[IRP_MJ_PNP] = DispatchPnp;

	driverObject->MajorFunction[IRP_MJ_INTERNAL_DEVICE_CONTROL] = DispatchInternalIoctl;


	return STATUS_SUCCESS;
}

void DriverUnload(IN PDRIVER_OBJECT driverObject)
{
	PAGED_CODE();
	UNREFERENCED_PARAMETER(driverObject);
	EngineShutdown();
}

// Byte 9 of the keyboard report holds the top-right key. The USB-C Magic Keyboard (0320)
// has Lock there (bit 3). The Magic Keyboard 2 (2015, A1644: USB 05AC:0267, Bluetooth
// 004C:0267) has Eject there instead and reports it as bit 0. Fn is bit 1 on both.
static UCHAR GetLockMask(PDEVICE_OBJECT pdo)
{
	PAGED_CODE();

	// Hardware IDs, e.g. USB\VID_05AC&PID_0267&REV_0098&MI_01 or
	// BTHENUM\{00001124-0000-1000-8000-00805f9b34fb}_VID&0001004c_PID&0267 (REG_MULTI_SZ).
	WCHAR ids[512];
	ULONG length = 0;
	RtlZeroMemory(ids, sizeof(ids));
	NTSTATUS status = IoGetDeviceProperty(pdo, DevicePropertyHardwareID, sizeof(ids) - 2 * sizeof(WCHAR), ids, &length);
	if (!NT_SUCCESS(status))
	{
		DebugPrint("GetLockMask(): IoGetDeviceProperty failed: 0x%x", status);
		return 0x08;
	}
	for (ULONG i = 0; i < ARRAYSIZE(ids); ++i)
		if (ids[i] >= L'a' && ids[i] <= L'z')
			ids[i] = (WCHAR)(ids[i] - L'a' + L'A');
	for (PWCHAR id = ids; *id; id += wcslen(id) + 1)
		if (wcsstr(id, L"VID_05AC&PID_0267") || wcsstr(id, L"004C_PID&0267"))
			return 0x01;
	return 0x08;
}

NTSTATUS AddDevice(IN PDRIVER_OBJECT driverObject, IN PDEVICE_OBJECT pdo)
{
	PAGED_CODE();

	DebugPrint("AddDevice(): driverObject: 0x%p, pdo: 0x%p", driverObject, pdo);

	//Create the filter functional device object
	PDEVICE_OBJECT fido;
	ULONG type = GetLowerDeviceType(pdo);
	NTSTATUS status = IoCreateDevice(driverObject, sizeof(DEVICE_EXTENSION), NULL, type, 0, FALSE, &fido);
	if (!NT_SUCCESS(status))
	{
		DebugPrint("AddDevice(): IoCreateDevice failed: 0x%x", status);
		return status;
	}

	//Add extension data to the fido
	PDEVICE_EXTENSION fidoExt = (PDEVICE_EXTENSION)fido->DeviceExtension;
	RtlZeroMemory(fidoExt, sizeof(DEVICE_EXTENSION));
	do
	{
		IoInitializeRemoveLock(&fidoExt->RemoveLock, 0, 0, 0);
		fidoExt->Pdo = pdo;
		fidoExt->LockMask = GetLockMask(pdo);

		//Add fido to the stack and propagate critical settings from the immediately lower device object
		PDEVICE_OBJECT fdo = IoAttachDeviceToDeviceStack(fido, pdo);
		if (!fdo)
		{
			DebugPrint("AddDevice(): IoAttachDeviceToDeviceStack failed");
			status = STATUS_DEVICE_REMOVED;
			break;
		}
		fidoExt->LowerDeviceObject = fdo;

		//Copy the flags related to I/O buffering from the lower device object so the I/O manager
		//will create the expected data structures for reads and writes.
		fido->Flags |= fdo->Flags & (DO_DIRECT_IO | DO_BUFFERED_IO | DO_POWER_PAGABLE);

		fidoExt->BatteryWork = IoAllocateWorkItem(fido);

		fido->Flags &= ~DO_DEVICE_INITIALIZING; // Clear the DO_DEVICE_INITIALIZING flag so that we can get IRPs

	} while (FALSE);

	if (!NT_SUCCESS(status))
	{
		if (fidoExt->LowerDeviceObject)
			IoDetachDevice(fidoExt->LowerDeviceObject);

		IoDeleteDevice(fido);
		return status;
	}

	KeWaitForSingleObject(&g_ControlGate, Executive, KernelMode, FALSE, NULL);
	if (g_FilterCount++ == 0)
		CreateControl(driverObject);
	if (!g_Control)
	{
		PIO_WORKITEM retry = IoAllocateWorkItem(fido);
		if (retry)
			IoQueueWorkItem(retry, RetryControl, DelayedWorkQueue, retry);
	}
	KeSetEvent(&g_ControlGate, IO_NO_INCREMENT, FALSE);

	return status;
}

void RemoveDevice(IN PDEVICE_OBJECT fido)
{
	PAGED_CODE();

	KeWaitForSingleObject(&g_ControlGate, Executive, KernelMode, FALSE, NULL);
	if (--g_FilterCount == 0)
		DeleteControl();
	KeSetEvent(&g_ControlGate, IO_NO_INCREMENT, FALSE);

	PDEVICE_EXTENSION fidoExt = (PDEVICE_EXTENSION)fido->DeviceExtension;
	if (fidoExt->BatteryWork)
		IoFreeWorkItem(fidoExt->BatteryWork); // the remove lock wait ensured it isn't running
	if (fidoExt->LowerDeviceObject)
		IoDetachDevice(fidoExt->LowerDeviceObject);

	IoDeleteDevice(fido);
}

ULONG GetLowerDeviceType(PDEVICE_OBJECT pdo)
{
	PDEVICE_OBJECT lowerPdo = IoGetAttachedDeviceReference(pdo);
	if (!lowerPdo)
		return FILE_DEVICE_UNKNOWN;

	ULONG devtype = lowerPdo->DeviceType;
	ObDereferenceObject(lowerPdo);

	return devtype;
}

NTSTATUS CompleteRequest(IN PIRP irp, IN NTSTATUS status, IN ULONG_PTR info)
{
	irp->IoStatus.Status = status;
	irp->IoStatus.Information = info;
	IoCompleteRequest(irp, IO_NO_INCREMENT);

	return status;
}

NTSTATUS DispatchPower(IN PDEVICE_OBJECT fido, IN PIRP irp)
{
	if (IsControl(fido))
		return DispatchControl(fido, irp);

	PDEVICE_EXTENSION pdx = (PDEVICE_EXTENSION)fido->DeviceExtension;
	PoStartNextPowerIrp(irp);

	NTSTATUS status = IoAcquireRemoveLock(&pdx->RemoveLock, irp);
	if (!NT_SUCCESS(status))
		return CompleteRequest(irp, status, 0);

	IoSkipCurrentIrpStackLocation(irp);

	status = PoCallDriver(pdx->LowerDeviceObject, irp);

	IoReleaseRemoveLock(&pdx->RemoveLock, irp);

	return status;
}

NTSTATUS DispatchPnp(IN PDEVICE_OBJECT fido, IN PIRP irp)
{
	if (IsControl(fido))
		return DispatchControl(fido, irp);

	PIO_STACK_LOCATION stack = IoGetCurrentIrpStackLocation(irp);
	ULONG fcn = stack->MinorFunction;
	PDEVICE_EXTENSION pdx = (PDEVICE_EXTENSION)fido->DeviceExtension;

	NTSTATUS status = IoAcquireRemoveLock(&pdx->RemoveLock, irp);
	if (!NT_SUCCESS(status))
		return CompleteRequest(irp, status, 0);

	//Handle usage notification specially in order to track power pageable
	//flag correctly. We need to avoid allowing a non-pageable handler to be
	//layered on top of a pageable handler.
	if (fcn == IRP_MN_DEVICE_USAGE_NOTIFICATION)
	{
		if (!fido->AttachedDevice || (fido->AttachedDevice->Flags & DO_POWER_PAGABLE))
			fido->Flags |= DO_POWER_PAGABLE;

		IoCopyCurrentIrpStackLocationToNext(irp);
		IoSetCompletionRoutine(irp, (PIO_COMPLETION_ROUTINE)UsageNotificationCompletionRoutine, (PVOID)pdx, TRUE, TRUE, TRUE);

		return IoCallDriver(pdx->LowerDeviceObject, irp);
	}

	//Start the lower stack first, then the virtual HID device that carries mapped actions
	if (fcn == IRP_MN_START_DEVICE)
	{
		status = IoForwardIrpSynchronously(pdx->LowerDeviceObject, irp) ? irp->IoStatus.Status : STATUS_UNSUCCESSFUL;
		if (NT_SUCCESS(status))
		{
			if (!pdx->Vhf)
			{
				VHFHANDLE vhf;
				NTSTATUS vhfStatus = EngineCreateVhf(fido, &vhf);
				if (NT_SUCCESS(vhfStatus))
					EngineStart(pdx, vhf);
				else
					DebugPrint("DispatchPnp(): virtual HID device failed: 0x%x", vhfStatus);
			}
		}
		CompleteRequest(irp, status, irp->IoStatus.Information);
		IoReleaseRemoveLock(&pdx->RemoveLock, irp);
		return status;
	}

	if (fcn == IRP_MN_SURPRISE_REMOVAL)
		EngineDetach(pdx, FALSE);

	//Handle remove device specially in order to cleanup device stack
	if (fcn == IRP_MN_REMOVE_DEVICE)
	{
		// After EngineDetach no report, timer or keyboard switch uses this VHF handle.
		VHFHANDLE vhf = EngineDetach(pdx, TRUE);
		if (vhf)
			VhfDelete(vhf, TRUE);
		IoSkipCurrentIrpStackLocation(irp);

		status = IoCallDriver(pdx->LowerDeviceObject, irp);

		IoReleaseRemoveLockAndWait(&pdx->RemoveLock, irp);

		RemoveDevice(fido);

		return status;
	}

	//Simply forward any other type of PnP request
	IoSkipCurrentIrpStackLocation(irp);
	status = IoCallDriver(pdx->LowerDeviceObject, irp);

	IoReleaseRemoveLock(&pdx->RemoveLock, irp);

	return status;
}

NTSTATUS UsageNotificationCompletionRoutine(PDEVICE_OBJECT fido, PIRP irp, PDEVICE_EXTENSION pdx)
{
	if (irp->PendingReturned)
		IoMarkIrpPending(irp);

	// If lower driver cleared pageable flag, we must do the same
	if (!(pdx->LowerDeviceObject->Flags & DO_POWER_PAGABLE))
		fido->Flags &= ~DO_POWER_PAGABLE;

	IoReleaseRemoveLock(&pdx->RemoveLock, irp);

	return STATUS_SUCCESS;
}

NTSTATUS DispatchAny(IN PDEVICE_OBJECT fido, IN PIRP irp)
{
	if (IsControl(fido))
		return DispatchControl(fido, irp);

	PDEVICE_EXTENSION devExt = (PDEVICE_EXTENSION)fido->DeviceExtension;
	NTSTATUS status;

	status = IoAcquireRemoveLock(&devExt->RemoveLock, irp);
	if (!NT_SUCCESS(status))
		return CompleteRequest(irp, status, 0);

	IoSkipCurrentIrpStackLocation(irp);

	status = IoCallDriver(devExt->LowerDeviceObject, irp);

	IoReleaseRemoveLock(&devExt->RemoveLock, irp);

	return status;
}


/* Run each completed keyboard input report (report ID 1) through the mapping
   engine, which rewrites it in place before HidUsb sees it. */
NTSTATUS InternalIoctlComplete(PDEVICE_OBJECT device, PIRP irp, PVOID context)
{
    UNREFERENCED_PARAMETER(device);
    PDEVICE_EXTENSION extension = (PDEVICE_EXTENSION)context;
    PIO_STACK_LOCATION stack = IoGetCurrentIrpStackLocation(irp);
    PURB urb = (PURB)stack->Parameters.Others.Argument1;

    if (NT_SUCCESS(irp->IoStatus.Status) && urb &&
        urb->UrbHeader.Function == URB_FUNCTION_BULK_OR_INTERRUPT_TRANSFER &&
        USBD_SUCCESS(urb->UrbHeader.Status))
    {
        struct _URB_BULK_OR_INTERRUPT_TRANSFER* transfer = &urb->UrbBulkOrInterruptTransfer;
        if ((transfer->TransferFlags & USBD_TRANSFER_DIRECTION_IN) && transfer->TransferBufferLength == MAGICKB_REPORT_LENGTH)
        {
            UCHAR* report = (UCHAR*)transfer->TransferBuffer;
            if (!report && transfer->TransferBufferMDL)
                report = MmGetSystemAddressForMdlSafe(transfer->TransferBufferMDL, NormalPagePriority | MdlMappingNoExecute);
            if (report && report[0] == 1)
                EngineProcessReport(report, extension);
        }
    }

    if (irp->PendingReturned)
        IoMarkIrpPending(irp);
    IoReleaseRemoveLock(&extension->RemoveLock, irp);
    return STATUS_CONTINUE_COMPLETION;
}

// Writes the newest battery level to the PDO's Bluetooth battery property (PASSIVE_LEVEL).
static VOID BatteryWorker(PDEVICE_OBJECT fido, PVOID context)
{
    UNREFERENCED_PARAMETER(context);
    PDEVICE_EXTENSION extension = (PDEVICE_EXTENSION)fido->DeviceExtension;
    InterlockedExchange(&extension->BatteryQueued, 0);
    // Write until the newest level is the one shown (a report may arrive during a write).
    for (int attempts = 0; attempts < 4; ++attempts)
    {
        LONG level = InterlockedCompareExchange(&extension->BatteryPending, 0, 0);
        if (level == InterlockedCompareExchange(&extension->BatteryShown, 0, 0))
            break;
        UCHAR value = (UCHAR)level;
        NTSTATUS status = IoSetDevicePropertyData(extension->Pdo, &DEVPKEY_OrchardBluetoothBattery, LOCALE_NEUTRAL, 0,
            DEVPROP_TYPE_BYTE, sizeof(value), &value);
        if (!NT_SUCCESS(status))
        {
            DebugPrint("BatteryWorker(): IoSetDevicePropertyData failed: 0x%x", status);
            break;
        }
        InterlockedExchange(&extension->BatteryShown, level);
    }
    IoReleaseRemoveLock(&extension->RemoveLock, extension->BatteryWork);
}

// Report 0x90 ([status][charge 0-100]) arrives unsolicited and as the reply to the tray
// app's GET_REPORT; publish a changed level from a work item.
static void NoteBattery(PDEVICE_EXTENSION extension, UCHAR level)
{
    if (level < 1 || level > 100 || !extension->BatteryWork)
        return;
    InterlockedExchange(&extension->BatteryPending, level);
    if (level == extension->BatteryShown || InterlockedCompareExchange(&extension->BatteryQueued, 1, 0) != 0)
        return;
    if (!NT_SUCCESS(IoAcquireRemoveLock(&extension->RemoveLock, extension->BatteryWork)))
    {
        InterlockedExchange(&extension->BatteryQueued, 0);
        return;
    }
    IoQueueWorkItem(extension->BatteryWork, BatteryWorker, DelayedWorkQueue, NULL);
}

/* Bluetooth: HidBth reads the HID interrupt channel with L2CAP ACL transfers. An input
   report arrives as a HID DATA|INPUT header (0xA1) followed by the same 10-byte report
   (ID 1) the keyboard sends over USB, so the same engine rewrites it in place. */
NTSTATUS BthComplete(PDEVICE_OBJECT device, PIRP irp, PVOID context)
{
    UNREFERENCED_PARAMETER(device);
    PDEVICE_EXTENSION extension = (PDEVICE_EXTENSION)context;
    PIO_STACK_LOCATION stack = IoGetCurrentIrpStackLocation(irp);
    struct _BRB_L2CA_ACL_TRANSFER* transfer = (struct _BRB_L2CA_ACL_TRANSFER*)stack->Parameters.Others.Argument1;

    if (NT_SUCCESS(irp->IoStatus.Status) && transfer)
    {
        // Bytes received: bthddi.h updates BufferSize to the bytes read for short transfers,
        // and it never exceeds the buffer, so reads stay inside it.
        ULONG length = transfer->BufferSize;
        UCHAR* data = NULL;
        if (length >= 2)
        {
            data = (UCHAR*)transfer->Buffer;
            if (!data && transfer->BufferMDL)
                data = MmGetSystemAddressForMdlSafe(transfer->BufferMDL, NormalPagePriority | MdlMappingNoExecute);
        }
        if (data && data[0] == 0xA1)
        {
            if (data[1] == 1 && length >= MAGICKB_REPORT_LENGTH + 1)
                EngineProcessReport(data + 1, extension);
            else if (data[1] == 0x90 && length >= 4)
                NoteBattery(extension, data[3]); // [0xA1][0x90][status][charge]
        }
    }

    if (irp->PendingReturned)
        IoMarkIrpPending(irp);
    IoReleaseRemoveLock(&extension->RemoveLock, irp);
    return STATUS_CONTINUE_COMPLETION;
}

NTSTATUS DispatchInternalIoctl(PDEVICE_OBJECT device, PIRP irp)
{
    if (IsControl(device))
        return DispatchControl(device, irp);

    PDEVICE_EXTENSION extension = (PDEVICE_EXTENSION)device->DeviceExtension;
    PIO_STACK_LOCATION stack = IoGetCurrentIrpStackLocation(irp);
    NTSTATUS status = IoAcquireRemoveLock(&extension->RemoveLock, irp);
    if (!NT_SUCCESS(status))
        return CompleteRequest(irp, status, 0);
    if (stack->Parameters.DeviceIoControl.IoControlCode == IOCTL_INTERNAL_USB_SUBMIT_URB)
    {
        IoCopyCurrentIrpStackLocationToNext(irp);
        IoSetCompletionRoutine(irp, InternalIoctlComplete, extension, TRUE, TRUE, TRUE);
        return IoCallDriver(extension->LowerDeviceObject, irp);
    }
    if (stack->Parameters.DeviceIoControl.IoControlCode == IOCTL_INTERNAL_BTH_SUBMIT_BRB)
    {
        struct _BRB_HEADER* brb = (struct _BRB_HEADER*)stack->Parameters.Others.Argument1;
        if (brb && brb->Type == BRB_L2CA_ACL_TRANSFER &&
            (((struct _BRB_L2CA_ACL_TRANSFER*)brb)->TransferFlags & ACL_TRANSFER_DIRECTION_IN))
        {
            IoCopyCurrentIrpStackLocationToNext(irp);
            IoSetCompletionRoutine(irp, BthComplete, extension, TRUE, TRUE, TRUE);
            return IoCallDriver(extension->LowerDeviceObject, irp);
        }
    }

    IoSkipCurrentIrpStackLocation(irp);
    status = IoCallDriver(extension->LowerDeviceObject, irp);
    IoReleaseRemoveLock(&extension->RemoveLock, irp);
    return status;
}
