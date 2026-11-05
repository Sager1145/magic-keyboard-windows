#pragma once

#include <ntifs.h> // IoGetRequestorSessionId; includes ntddk.h and wdm.h
#include <wdmsec.h>
#include <vhf.h>
#include "usbioctl.h"
#include "usbdi.h"
#include <bthdef.h>
#include <bthioctl.h>
#include <bthddi.h>

#ifdef __cplusplus
extern "C" {
#endif

	#define DRIVERNAME "MagicKeyboardFn"

	#if DBG
		#define DebugPrint(s, ...) DbgPrint(DRIVERNAME ": " s "\n", __VA_ARGS__)
	#else
		#define DebugPrint(...) ((void)0)
	#endif

	///////////////////////////////////////////////////////////////////////////////
	// Interface shared with gui\DriverConfig.cs and gui\DriverCapture.cs
	//
	// \\.\MagicKeyboardFn
	//   READ_EVENT  returns one MAGICKB_EVENT (pends until one is available)
	//   GET_CONFIG  returns the active MAGICKB_CONFIG
	//   SET_CONFIG  validates, applies and persists a MAGICKB_CONFIG
	#define IOCTL_MAGICKB_READ_EVENT CTL_CODE(0x9A4B, 0x801, METHOD_BUFFERED, FILE_READ_ACCESS)
	#define IOCTL_MAGICKB_GET_CONFIG CTL_CODE(0x9A4B, 0x802, METHOD_BUFFERED, FILE_READ_ACCESS)
	#define IOCTL_MAGICKB_SET_CONFIG CTL_CODE(0x9A4B, 0x803, METHOD_BUFFERED, FILE_WRITE_ACCESS)

	#define MAGICKB_REPORT_LENGTH 10
	#define MAGICKB_RING_LENGTH 64

	// Action types. Row keys hold KEY/CONSUMER/SYSTEM while pressed; CHORD is
	// always a tap. Gestures tap every type. APP is run by the tray app.
	#define MAGICKB_ACTION_NONE     0
	#define MAGICKB_ACTION_KEY      1 // Modifiers + keyboard usage (page 0x07)
	#define MAGICKB_ACTION_CHORD    2 // Modifiers + keyboard usage, tapped
	#define MAGICKB_ACTION_CONSUMER 3 // consumer usage (page 0x0C), e.g. 0xE9 volume up
	#define MAGICKB_ACTION_SYSTEM   4 // generic desktop 0x81 power, 0x82 sleep, 0x83 wake
	#define MAGICKB_ACTION_APP      5 // posted to the tray app; without a tray, Modifiers + Usage (if set) are tapped instead
	#define MAGICKB_ACTION_STANDARD 6 // pass the original F-key through
	#define MAGICKB_ACTION_MAX      6

	#define MAGICKB_FLAG_ACTIONS_FIRST 0x1 // actions without Fn; hold Fn for F1-F12

	#define MAGICKB_CONFIG_VERSION 2
	#define MAGICKB_GESTURE_LOCK 0
	#define MAGICKB_GESTURE_FN   1

	typedef struct tagMAGICKB_ACTION
	{
		UCHAR Type;
		UCHAR Modifiers; // keyboard modifier bits: 1 Ctrl, 2 Shift, 4 Alt, 8 Win
		USHORT Usage;
	} MAGICKB_ACTION;

	typedef struct tagMAGICKB_CONFIG
	{
		ULONG Version;
		ULONG Flags;
		USHORT DoubleTapMs;
		USHORT LongPressMs; // Fn long press, evaluated at release
		USHORT LockHoldMs; // Lock long press, fires while held
		USHORT Reserved;
		MAGICKB_ACTION Row[12]; // action layer of F1-F12
		MAGICKB_ACTION Gesture[2][3]; // [Lock, Fn][single, double, long]
		// Modifier remap: for each modifier bit of the keyboard's report (0 LCtrl, 1 LShift,
		// 2 LAlt/Option, 3 LGui/Command, 4-7 the right-hand ones), the bit it becomes.
		// Identity is { 0, 1, ..., 7 }.
		UCHAR ModifierMap[8];
	} MAGICKB_CONFIG;

	#define MAGICKB_EVENT_REPORT 1 // Report holds the physical report as received
	#define MAGICKB_EVENT_APP    2 // run the app action for Key (0-11 F-row, 12 Lock, 13 Fn) / Gesture

	typedef struct tagMAGICKB_EVENT
	{
		ULONG Sequence; // increments per event; a gap means the ring overflowed
		UCHAR Kind;
		UCHAR Key;
		UCHAR Gesture;
		UCHAR Reserved;
		UCHAR Report[MAGICKB_REPORT_LENGTH];
		UCHAR Pad[2];
	} MAGICKB_EVENT;

	// Device extension shared by the filter objects and the control object
	typedef struct tagDEVICE_EXTENSION
	{
		BOOLEAN IsControl; // TRUE only for \Device\MagicKeyboardFn
		BOOLEAN Deleted; // control object removed; open handles get STATUS_DEVICE_REMOVED
		PDEVICE_OBJECT LowerDeviceObject; // next lower driver in same stack
		PDEVICE_OBJECT Pdo; // the PDO
		IO_REMOVE_LOCK RemoveLock;
		VHFHANDLE Vhf; // virtual HID device that carries the mapped actions (engine lock)
		BOOLEAN EngineDetached; // removal started: reports pass through, Vhf is not used
		// Bluetooth battery level shown by Windows (DEVPKEY_Bluetooth_Battery on the PDO).
		PIO_WORKITEM BatteryWork; // writes the property at PASSIVE_LEVEL
		LONG BatteryShown; // level last written, 0 = none
		LONG BatteryPending; // level to write next
		LONG BatteryQueued; // 1 while BatteryWork is queued
	} DEVICE_EXTENSION, *PDEVICE_EXTENSION;

	NTSTATUS DriverEntry(IN PDRIVER_OBJECT driverObject, IN PUNICODE_STRING registryPath);
	void DriverUnload(IN PDRIVER_OBJECT fido);
	void RemoveDevice(IN PDEVICE_OBJECT fdo);
	NTSTATUS CompleteRequest(IN PIRP Irp, IN NTSTATUS status, IN ULONG_PTR info);
	NTSTATUS DispatchAny(IN PDEVICE_OBJECT fido, IN PIRP irp);
	NTSTATUS DispatchPower(IN PDEVICE_OBJECT fido, IN PIRP irp);
	NTSTATUS DispatchPnp(IN PDEVICE_OBJECT fido, IN PIRP irp);
	NTSTATUS DispatchControl(IN PDEVICE_OBJECT control, IN PIRP irp);
	ULONG GetLowerDeviceType(PDEVICE_OBJECT pdo);
	NTSTATUS UsageNotificationCompletionRoutine(PDEVICE_OBJECT fido, PIRP irp, PDEVICE_EXTENSION pdx);
	NTSTATUS InternalIoctlComplete(IN PDEVICE_OBJECT fido, IN PIRP irp, IN PVOID context);
	NTSTATUS DispatchInternalIoctl(IN PDEVICE_OBJECT fido, IN PIRP irp);

	// Mapping engine (Mapping.c)
	void EngineInitialize(PDRIVER_OBJECT driverObject);
	void EngineStart(PDEVICE_EXTENSION extension, VHFHANDLE vhf);
	VHFHANDLE EngineDetach(PDEVICE_EXTENSION extension, BOOLEAN remove);
	void EngineShutdown(void);
	void EngineProcessReport(UCHAR* report, PDEVICE_EXTENSION extension);
	void EngineGetConfig(MAGICKB_CONFIG* config);
	NTSTATUS EngineSetConfig(const MAGICKB_CONFIG* config);
	NTSTATUS EngineCreateVhf(PDEVICE_OBJECT fido, VHFHANDLE* vhf);

	// Event ring (Driver.c), callable at <= DISPATCH_LEVEL
	void PublishEvent(UCHAR kind, UCHAR key, UCHAR gesture, const UCHAR* report);
	BOOLEAN ReaderConnected(void);

#ifdef __cplusplus
}
#endif
