# Security policy

Couchtop can replace the Windows shell for the signed-in user, so security and recoverability issues are taken seriously.

## Supported versions

Only the latest `0.x` pre-release receives fixes.

## Reporting a vulnerability

Please **do not** open a public issue. Report privately through
[GitHub private vulnerability reporting](https://github.com/TheAgencyMGE/couchtop/security/advisories/new).

Please include:

- The Couchtop version and your Windows version and edition
- Steps to reproduce
- The impact, for example privilege escalation, persistence, or a way to block recovery or trap the user at a black screen

You should receive an initial response within a few days.

## Scope notes

- Couchtop runs as the signed-in user and never requests administrator rights.
- Shell mode writes only `HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell`.
- The Web channel uses Microsoft Edge WebView2. Browser engine vulnerabilities should be reported to Microsoft.
