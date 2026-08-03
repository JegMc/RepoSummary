# RepoSummary browser extension

Adds a floating **"Analyze in RepoSummary"** button to any GitHub repository page that
deep-links straight into your local RepoSummary app — DeepWiki's "swap the URL" trick,
but pointed at your own instance.

## Configure

The button points at `http://localhost:5080` by default. If your app runs elsewhere
(e.g. the HTTPS profile on `https://localhost:7080`), edit the `HOST` constant at the top
of [`content.js`](./content.js).

## Install (Chrome / Edge — unpacked)

1. Run RepoSummary locally (`cd RepoSummary && dotnet run`).
2. Open `chrome://extensions` (or `edge://extensions`).
3. Turn on **Developer mode**.
4. Click **Load unpacked** and select this `browser-extension` folder.
5. Visit any `github.com/owner/repo` page — a green **Analyze in RepoSummary** button
   appears bottom-right. Click it to open that repo's analysis.

## Install (Firefox — temporary)

1. Open `about:debugging#/runtime/this-firefox`.
2. Click **Load Temporary Add-on…** and pick `manifest.json` in this folder.

## Notes

- Manifest V3, single content script, no permissions beyond running on `github.com`.
- `icon.png` is optional; add a 128×128 PNG here if you want a toolbar icon.
