# keypaste privacy policy

Draft version 2026-11-19, not yet in effect.

Dan I Ochoa Barrios, an individual doing business as keypaste ("keypaste", "we", "us"), publishes keypaste and runs its services. This policy says what keypaste's app, command line, MCP bridge, browser extension, services and website send to us, what we keep, and what you can ask of us. Our [terms of use](https://keypaste.com/terms/) cover the rest.

## What we never receive

Your vault, your master password, your key file, the secrets, notes and names inside your vault, your activity log, and which apps and agents you allow. keypaste keeps your vault in a file where you choose and never sends it to us. The command line and the MCP bridge send us nothing. The browser extension talks only to the keypaste app on your own computer.

## Crash reports

Crash reports are off unless you turn them on, with Send crash reports on the app's first screen or in Settings. While they're on, the desktop app sends a report when it stops working or hits an error it didn't expect.

A report holds: keypaste's version and build; your operating system, its version number and your processor type; the .NET version; roughly how long the app had been running; which part of the app was open; the date; the type of error; and the names of keypaste's code functions involved, with their positions in the code.

A report never holds: anything from your vault; your master password or key file; error messages; file names or paths; environment variables; agent or app names; your activity log; your user or computer name; or any identifier for you or your installation.

A report waits on your computer until it can be sent, and you can see it under Settings › Advanced › Diagnostics. Turning reports off stops collection and deletes reports waiting to be sent. Reports go over HTTPS to api.keypaste.com. We keep a report 90 days after the same failure was last reported, and Cloudflare's backups can hold it up to 30 more. Only keypaste's maintainers read them. Because a report holds no identifier, we can't find your particular report; if you ask, we can delete every report for a given version.

## Share links

`keypaste share` encrypts a value on your computer before uploading it. Our services store only the encrypted value, which they can't open because the key stays in the link, along with how many views are left, when the link expires, and a fingerprint of the code that cancels it. A link opens 1 to 10 times within 5 minutes to 7 days. It is deleted after its last view, within about 30 minutes after it expires, or when you cancel it.

## The launch list

If you sign up on keypaste.com to hear about keypaste, we store your email address and when you signed up, and nothing else. We use it only to tell you about keypaste. To be removed, email contact@keypaste.com from that address and we will delete it.

## The website

keypaste.com sets no cookies and runs no analytics or advertising. The home and download pages ask GitHub for keypaste's star count from your browser, so GitHub sees that request under [its own privacy statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).

## Your IP address

Every request to our services and website reaches Cloudflare, which sees your IP address to deliver it. Our services use it for a moment to limit how many requests one address can make, and we don't store it or log it with what you sent.

## Who processes data for us

- **Cloudflare**, Inc. runs our services and website and stores crash reports.
- **PlanetScale**, Inc. stores share links and the launch list, in the United States (AWS us-east-1).

We don't sell or share personal information, and we don't use it for advertising. We disclose information only to these processors or when the law requires it.

## Your choices and rights

You can turn crash reports off at any time, cancel a share link you made, and ask us to delete your email address. Depending on where you live, such as California, the European Union or the United Kingdom, you may have the right to know what personal information we hold about you, to have it corrected or deleted, and to complain to a data protection authority. Email contact@keypaste.com and we will answer within 30 days. We hold almost nothing that identifies you: at most your email address, if you signed up.

Our services are in the United States, so information you send them is processed there.

## Children

keypaste isn't directed at children under 13, and we don't knowingly collect their personal information.

## Changes

We post changes at keypaste.com/privacy with a new date. If a change affects crash reports, the app stops sending them until you agree to the new terms.

## Contact

contact@keypaste.com
