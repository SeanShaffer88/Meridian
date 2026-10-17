# Meridian screenshot standards

The README should become a visual tour of real, implemented features as Windows runtime verification becomes available.

## Capture rules

- Capture **the actual running Meridian application**, not a generated mockup, design render or edited composite passed off as a screenshot.
- Confirm the feature works on the exact commit or build associated with the screenshot.
- Use disposable demonstration folders and media. Never expose personal filenames, addresses, accounts, credentials, file paths containing user names or other private data.
- Use consistent Windows 11 display scaling, window dimensions and app theme where practical. Crop only to focus the interface; do not hide failures or change feature behavior.
- Save optimized PNG or WebP images in `docs/images/screenshots/` with descriptive names such as `dual-pane-workspace.png`, `folder-dna.png`, `drop-shelf.png`, `operation-simulator.png`, `universal-viewer-video.png` and `timevault-snapshot-picker.png`.
- Add each verified image to the README near the corresponding feature description with concise alt text, a caption, and a clear status if the feature is still experimental.
- Keep screenshots current: replace them when layouts change materially, and remove screenshots of features that have been removed.
- Do not add broken image links or claim captures exist before files are committed.

## Recommended gallery order

1. Dual-pane workspace, tabs and navigation
2. Folder DNA
3. Drop Shelf and operation preview
4. Universal Viewer: text, images and native video controls
5. TimeVault read-only snapshot exploration
6. New tools when implemented and validated

## Verification record

For each image, record in its pull request or commit message: commit SHA, Windows build/architecture, capture date, and a brief description of the action tested. If a screenshot cannot be captured in a verified Windows environment, leave its gallery entry pending rather than substituting a conceptual image.

## Importing a verified capture

On the Windows machine where Meridian was built and tested, capture a PNG of the **real running application** and review it for private data. From the repository root:

```powershell
./scripts/Import-FeatureScreenshot.ps1 -Feature dual-pane-workspace -Source ./capture.png -VerifiedCommit <full-40-character-commit-sha> -ConfirmedRealCapture
```

The importer checks the PNG signature and dimensions, confirms the commit exists in the local Git repository, and refuses to overwrite a prior capture. **It cannot independently prove** that the image depicts Meridian or that the feature worked; the reviewer must verify those facts. The screenshot is copied to `docs/images/screenshots/`; it is not automatically published in the README. Add an accessible Markdown image and caption only after review.

Do not copy the literal angle-bracket placeholder in the example; replace it with the verified build commit.
