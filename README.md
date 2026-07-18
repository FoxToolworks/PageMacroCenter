# PageMacroCenter

PageMacroCenter is a lightweight EPLAN script for browsing, searching, previewing and inserting page macros (`.emp`). It reads the configured EPLAN macro directory through `$(MD_MACROS)` and does not require a hard-coded path.

## Features

- Browse page macros in a recursive folder tree
- Search by file name and relative path
- Multi-word search, for example `Klappe 230V`
- Search tolerant of spaces, underscores and German umlaut spellings (`ü` / `ue`)
- Open the native EPLAN macro preview by selecting a page macro
- Insert page macros by dragging them into EPLAN
- Refresh the macro directory with `F5`
- Clear the current search with `Esc`
- Keep a single non-modal PageMacroCenter window open

## Requirements

- EPLAN Platform with C# scripting support
- A configured macro directory (`$(MD_MACROS)`)
- Page macros stored as `.emp` files

Version 0.3.4 has been developed and tested with EPLAN 2025. Other EPLAN versions may behave differently, particularly the native preview action.

## Installation and use

1. Download `PageMacroCenter.cs`.
2. In EPLAN, open **File > Extras > Interfaces > Scripts > Run**.
3. Select `PageMacroCenter.cs`.
4. Type into the search field to filter the tree.
5. Select a page macro to show it in the native EPLAN preview.
6. Drag a page macro from the tree into EPLAN to insert it.

The exact menu labels may vary with the EPLAN language and version.

## Keyboard shortcuts

| Key | Action |
| --- | --- |
| `F5` | Rescan the macro directory |
| `Esc` | Clear the search field |

## Known limitations

- The interface is currently available in German.
- The script uses EPLAN's `XSDPreviewAction`. Preview availability and behavior may vary between EPLAN versions.
- Window size and position are not persisted in version 0.3.4.
- Only EPLAN page macros (`.emp`) are displayed.

## License

PageMacroCenter is licensed under the [MIT License](LICENSE).

## Disclaimer

PageMacroCenter is an independent community project by FoxToolworks. It is not affiliated with, endorsed by or supported by EPLAN GmbH & Co. KG. EPLAN and related product names are trademarks of their respective owners.
