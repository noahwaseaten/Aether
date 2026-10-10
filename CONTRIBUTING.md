# Contributing

Bug reports and pull requests are welcome.

## Reporting a bug

Open an [issue](https://github.com/noahwaseaten/Aether/issues) with:

- your Aether version (shown in the Aether window)
- what you expected and what happened
- a screenshot if it's a display problem
- whether you were host or joined someone else's quest

## Pull requests

1. Fork the repo and branch off `main`.
2. Keep each PR to one change and explain what it fixes or adds.
3. Build and run the self-test before opening it:

   ```powershell
   .\build.ps1 -Version 0.0.0
   ```

4. Open the PR against `main`. It needs a review before it can be merged.

Don't bump the version or touch `build.ps1`'s publish step; releases are cut by the maintainer.

Aether only reads game memory. PRs that write to game memory, or anything that could get people banned, won't be merged.
