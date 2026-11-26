# Contributing to Yok-AI

Thanks for helping. Bug reports, ideas, docs fixes and code are all welcome.

## What you need

| Requirement | Version |
|---|---|
| Windows | 10 version 2004 or later, or 11 (capture hiding does not work on older builds) |
| Visual Studio | 2022 or later, with the **.NET desktop development** workload |
| .NET Framework | 4.7.2 developer pack (installed with the workload) |
| Git | Any recent version |
| An AI to test with | Your own Claude Code, an API key, or a free local model with [Ollama](https://ollama.com) |
| For transcription | An English Windows speech pack (Settings > Time & language > Speech) |

You do **not** need any key from the project. Everything runs with your own AI account or a local model.

## Get the code running

```powershell
git clone https://github.com/<owner>/<repo>.git
cd <repo>
msbuild StealthAssistWPF.sln -t:Restore -p:RestorePackagesConfig=true
msbuild StealthAssistWPF.sln -p:Configuration=Debug
.\StealthAssistWPF\bin\Debug\YokAI.exe --screenshot-mode
```

Or open `StealthAssistWPF.sln` in Visual Studio and press F5. Add `--screenshot-mode` under **Project > Properties > Debug > Command line arguments** so you can see the window in your own screenshots and screen recordings.

## Ground rules

- **Never commit keys, tokens, personal data or your `settings.dat`.** The `.gitignore` blocks the usual files; check `git diff` before every commit anyway.
- **No servers, analytics or telemetry.** Yok-AI talks only to the AI provider the user chose. Changes that send data anywhere else will not be accepted.
- Keep it one file: new libraries must work with Costura.Fody bundling into `YokAI.exe`.
- Keep features that hide the window (capture exclusion, Alt+Tab hiding) working, and keep `--screenshot-mode` working.
- Write plain user-facing text: say what happened and what to do next.

## Sending a change

1. Open an issue first for anything bigger than a small fix, so we can agree on the approach.
2. Fork the repo and create a branch: `fix/short-name` or `feature/short-name`.
3. Keep the change focused. Match the style of the code around it.
4. Build **Release** with no new errors and run the app. Check the things your change touches, plus: chat answers, a screenshot question, and transcription on/off.
5. Open a pull request and fill in the template. Add before/after screenshots (taken in `--screenshot-mode`) for anything visible.

## Reporting bugs and ideas

Use the issue forms. For bugs, include your Windows version, the AI provider you used, steps to reproduce, and what you expected. Remove keys and personal text from logs and screenshots.

Security problems: please follow [SECURITY.md](SECURITY.md) instead of opening a public issue.

## Code of conduct

Everyone taking part follows the [Code of Conduct](CODE_OF_CONDUCT.md).

## License of contributions

By sending a contribution you agree it is released under the project's [MIT License](LICENSE).
