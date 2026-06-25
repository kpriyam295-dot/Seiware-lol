// This file is not needed for Seiware integration.
// Session management is handled by FakeTerminalSession in Seiware.cs (backend)
// and FakeTerminal.cs connects to it via named pipe.
//
// IPC Protocol:
//   MSG_OUTPUT (0x01)       - Backend sends output text
//   MSG_CLEAR (0x02)        - Backend requests screen clear
//   MSG_SESSION_ENDED (0x03) - Backend terminated session
//   MSG_CMD_FINISHED (0x04)  - Backend finished command, show prompt
//   MSG_COMMAND (0x10)       - FakeTerminal sends command to backend
//
// See FakeTerminal.cs for the client implementation.
// See Seiware.cs FakeTerminalSession class for the server implementation.
