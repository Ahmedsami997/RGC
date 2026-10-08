#!/bin/bash
# Removes the RGC Announcements agent for the current macOS user.
LABEL="com.royalgolfclub.rgc.agent"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
launchctl bootout "gui/$(id -u)" "$PLIST" 2>/dev/null || true
pkill -x RGC.Agent 2>/dev/null || true
rm -f "$PLIST"
rm -rf /Applications/RGC.app "$HOME/Applications/RGC.app"
rm -rf "$HOME/Library/Application Support/RGC"
security delete-generic-password -s com.royalgolfclub.rgc.agent >/dev/null 2>&1 || true
echo "RGC has been removed from this Mac."
