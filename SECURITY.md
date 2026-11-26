# Security policy

## Supported versions

Only the latest release gets security fixes.

## Reporting a problem

Please **do not** open a public issue for security problems.

Report them privately through GitHub: open the repository's **Security** tab and click **Report a vulnerability**. Include:

- what the problem is and what someone could do with it,
- steps to reproduce,
- the Yok-AI version and Windows version.

You should get a reply within 7 days. Once a fix is released, we are happy to credit you if you want.

## What counts

Examples: keys or settings readable by other Windows users, data sent anywhere other than the provider the user chose, the window becoming visible in screen capture when it should not, or code running from untrusted input.

## How Yok-AI handles your data

- No Yok-AI server, account, analytics or telemetry.
- Keys are stored only in `%LOCALAPPDATA%\YokAI\settings.dat`, encrypted with Windows DPAPI for the current Windows user.
- Questions, screenshots and transcript text go only to the AI provider the user selected. Audio goes to Azure only if the user adds their own Azure key.
