# Couchtop trailer

The 16:9 trailer shown in the main README, built with [Remotion](https://www.remotion.dev).

- The screenshots come from `docs/screenshots` (rendered with `Couchtop.exe --render-snapshot`).
- The fonts and icon are copied from the main project by `npm run sync-assets`.
- The music and sound effects in `public/audio` are Couchtop's own synthesized sounds (`Couchtop.exe --export-audio public/audio`).

```bash
npm install
```

```bash
npm run dev
```

```bash
npm run render
```

```bash
npm run render:gif
```

`npm run dev` opens Remotion Studio. The two render scripts write `docs/media/couchtop-trailer.mp4` and the README preview `docs/media/couchtop-trailer.gif`.
