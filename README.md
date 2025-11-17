VR Player

This repository contains quick uploads of screenshots, notes, Unity scripts, and data files related to the VR Video App used in the Sundown XR Research Project. The project focuses on building a calming VR experience for the HTC Vive Focus Vision while collecting synchronized physiological and behavioral data from a Samsung Galaxy Watch 7.

Project Overview

The VR system provides:

* Immersive 360
* Local video support
* Automatic playlist management and simple UI
* Eye tracking, gaze direction, fixation, blink rate, and head pose tracking
* Timestamped session logging for research

Galaxy Watch 7 Integration:

* Heart rate
* HRV from IBI
* Accelerometer raw XYZ
* Stress index
* All values streamed over BLE using a custom service

Unity Integration:

* BluetoothManager.cs receives watch data and writes CSV rows
* EyeTrackingLogger.cs logs gaze, head pose, video name, and timestamps
* UIScreenManager.cs manages UI flow and session navigation
* SyncIntegration.cs ties BLE data to VR session start/end
* Additional helper scripts: WristUI.cs, VideoPlaylistController.cs, GenerateVideoLists.cs, BluetoothPermissionRequester.cs, GalleryTitleUpdater.cs

This repo acts as a progress log, data archive, and debugging history.

Repository Structure
/screenshots/ – UI captures, BLE logs, debugging tests
/data/ – Raw CSV logs, watch sensor data, eye tracking logs
/notes/ – Short explanation files for each upload
/scripts/ – Unity C# scripts used in current builds

Usage

* Folders are organized by date
* Screenshots document UI changes and test sessions
* Data files are raw and unedited
* Notes help track what each upload corresponds to
* This repository is not the full Unity project

Version Notes

* Watch source added 9/18/2025
* Unity scripts added 9/28/2025
* BLE watch to VR sync stabilized October 2025
* CSV parsing and combined logging currently being refined

Research Goals

* Improve BLE reliability for longer sessions
* Ensure no connection drop after 30 to 60 seconds
* Validate timestamp synchronization between eye tracking, head pose, heart rate, HRV, and accelerometer
* Prepare data for analysis
* Build tablet caregiver dashboard (Phase 2)

Disclaimer
This repo is used only for progress tracking and data collection. Full Unity project and builds are stored elsewhere.
