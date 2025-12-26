# Data Collected

This document provides a high-level overview of the data collected during a Sundown VR session.

---

## Behavioral & VR Data
Collected directly from the VR application:

- Eye gaze direction
- Blink events and blink rate
- Fixation-related metrics
- Head pose (position and rotation)
- Video identifier and playback context
- Session start and end timestamps

---

## Physiological Data (Galaxy Watch 7)
When enabled, the following signals are collected:

- Heart rate
- Inter-beat interval (IBI) for HRV analysis
- Raw accelerometer data (X, Y, Z)
- Stress-related metrics provided by watch APIs

---

## Data Format
- All data is logged in **CSV format**
- Files are timestamped and session-based
- Data is stored unedited to preserve raw signals

---

## Synchronization
- Behavioral and physiological data are aligned using shared session timing.
- Each session is associated with a unique identifier.
- Logs are structured to support downstream analysis and feature extraction.

---

## Privacy & Scope
- No personally identifying information is collected.
- Data is gathered only during active sessions.
- The system is intended for research infrastructure development.

