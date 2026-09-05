# Academy Community UI assets

Build 127 implements the selected Option 3 in the existing native MAUI app.

- Typeface: Nunito Sans from the official [Google Fonts source](https://github.com/google/fonts/tree/main/ofl/nunitosans), SIL Open Font License. Static weights 400/700 preserve Vietnamese accents. The old font aliases are intentionally retained so existing screens share the same typeface.
- Icons: [Tabler Icons v3.46.0](https://github.com/tabler/tabler-icons/tree/v3.46.0), MIT. Outline SVGs are imported from upstream, with `currentColor` mechanically resolved into forest or white for MAUI rasterization. `tools/Import-AcademyAssets.ps1` reproduces the import.
- Licenses ship in `Resources/Licenses` and in the application package.
- `academy_community_hero.png`: generated academy illustration, fictional coach and youth players in plain teal shirts, warm late-afternoon light. It is not a photograph of actual members. No generated portraits are used for individual accounts.
- Existing AWAKEN brand marks, splash screen, app icon and all 21 achievement PNGs are preserved. Missing individual profile photographs use name initials.

To regenerate static fonts after importing the variable source, install FontTools in an isolated tooling environment and run:

```text
python -m fontTools.varLib.instancer Resources/Fonts/NunitoSans-Variable.ttf wght=400 wdth=100 opsz=12 YTLC=500 -o Resources/Fonts/NunitoSans-Regular.ttf
python -m fontTools.varLib.instancer Resources/Fonts/NunitoSans-Variable.ttf wght=700 wdth=100 opsz=12 YTLC=500 -o Resources/Fonts/NunitoSans-Bold.ttf
```

The variable font is source material only and is excluded from packaged MAUI fonts.
