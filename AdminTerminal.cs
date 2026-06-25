// This file is not needed for Seiware integration.
// Admin mode is handled by passing --admin flag to FakeTerminal.
//
// Usage:
//   "Command Prompt.exe" --admin "terminal.png" --banned "word1|word2"
//
// Admin mode differences:
// - Title: "Administrator: Command Prompt" (vs "Command Prompt")
// - Starting directory: C:\Windows\System32 (vs user profile)
// - isAdmin=true in IPC messages to backend
//
// See FakeTerminal.cs constructor and InitializeConsole() for implementation.
