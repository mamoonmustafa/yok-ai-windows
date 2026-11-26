# Yok-AI features

Every feature in the Windows app, grouped by area.

## Staying private on screen

| Feature | Details |
|---|---|
| Hidden from screen capture | Uses the Windows `WDA_EXCLUDEFROMCAPTURE` setting, so screen sharing, recording and screenshot tools do not see the window. Needs Windows 10 version 2004 or later. |
| Hidden from Alt+Tab and taskbar | Runs as a tool window with no taskbar button. |
| Always on top, slightly see-through | Stays above other windows so you can read it during a call. |
| Global shortcuts | Show/hide, screenshot, multi-screenshot, keystroke protection and emergency exit work from any app. |
| Keystroke protection (`Ctrl+Alt+K`) | While on, everything you type goes only into Yok-AI's message box, not the app underneath. `Ctrl+Alt+H` does the same. |
| Emergency exit (`Ctrl+Alt+X`) | Closes the app at once and deletes your saved settings and keys. |
| Screenshot mode | Start with `--screenshot-mode` to turn capture hiding off for that run (for documentation and testing). |

## AI chat

| Feature | Details |
|---|---|
| Bring your own AI | Claude Code (your own install, found automatically), Claude API, OpenAI, Groq, OpenRouter, Ollama, or any OpenAI-compatible server. |
| Screenshot questions (`Ctrl+Alt+C` or 📷) | Captures your screen and attaches it to the next question. |
| Multi-screenshot (`Ctrl+Alt+M` then `Ctrl+Alt+F`) | Take several screenshots and send them together. Thumbnails show what is attached; remove them one by one or clear all. |
| Reply to an answer (↩) | Ask a follow-up about one specific answer. |
| Pin an answer (📌) | Focus mode shows just that answer and its follow-ups. |
| Chat memory (🧠) | On: the AI remembers the conversation. Off: every question starts fresh. |
| Formatted answers | Headings, bullet lists, bold text and code blocks are shown formatted. |

## Live meeting transcription

| Feature | Details |
|---|---|
| Free offline speech to text | Uses the speech recognizer built into Windows. No key, no internet; audio stays on your PC. |
| Optional Azure Speech | Add your own Azure Speech key and region for higher accuracy. |
| Hears the other people | Captures the computer's sound output (system loopback), so it hears everyone in the call without extra software. |
| Live and final text | Grey text updates while someone speaks; the final line gets a timestamp. |
| Voice detection | Detects speech and silence automatically and adapts to the noise level. |
| Speaker-change detection | Starts a new line when the speaker seems to change. |
| AI notes | Every few seconds, when there are new sentences, the AI adds key points, open questions and suggested actions. |
| Ask about any part | Select transcript text, or press Enter in the transcript, to send it to the AI. |
| Word count | Shows how many words have been transcribed. |
| Local recording | Saves the meeting audio as a WAV file in `%LOCALAPPDATA%\YokAI\Recordings`. |
| Export (⬇) | Saves the session (transcript, chat and stats) to a file you choose. |

## Meeting context

| Feature | Details |
|---|---|
| Default prompts (📋) | Set the instruction used for transcript lines, for screenshots and for selected text, e.g. "I am the interviewer for a backend role; suggest follow-up questions". Saved on your PC. |

## Settings

| Feature | Details |
|---|---|
| Provider list with hints | Each provider shows what key it needs and where to get it. |
| Model and server address | Choose a model, or point to your own OpenAI-compatible server. |
| Encrypted storage | Keys are encrypted with Windows DPAPI for your Windows account only. |
| Debug messages | Optional extra status lines for troubleshooting. |
