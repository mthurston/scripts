# Lucky Scratch — Defold Edition

A scratch-off lottery ticket vending machine built with [Defold](https://defold.com).

## Gameplay

- Start with **$10.00**
- Buy scratch tickets for **$1.00** each from the vending machine
- Click the 3×3 grid cells to reveal symbols
- **Match any 3 identical symbols** → win **$5.00**
- ~30% win rate per ticket

## Controls

| Input | Action |
|-------|--------|
| Click | Scratch an individual cell |
| `S` | Scratch all cells at once |
| `SPACE` | Buy ticket / Return to vending machine |
| `ESC` | Quit |

## Symbols

| Symbol | Display |
|--------|---------|
| Seven  | `7` (red) |
| Dollar | `$` (gold) |
| Star   | `*` (cyan) |
| Bar    | `BAR` (orange) |
| Cherry | `CH` (pink) |

## Prerequisites

- [Defold Editor](https://defold.com/download/) (free)

## Running

1. Open **Defold Editor**
2. **File → Open Project** → select `defold/ScratchTicket/game.project`
3. Press **Build** (`Ctrl+B` / `Cmd+B`) to run

## Project Structure

```
ScratchTicket/
├── game.project                    # Project settings (800×600, input bindings)
├── input/
│   └── game.input_bindings         # SPACE, S, ESC, mouse click
└── main/
    ├── main.collection             # Bootstrap collection
    ├── gui.go                      # Game object with GUI component
    ├── main.gui                    # All visual nodes (vending + scratch screens)
    ├── main.gui_script             # All game logic — screen switching, input, UI updates
    └── modules/
        ├── game_state.lua          # Player balance, win/loss tracking
        └── ticket.lua              # Ticket generation (30% win), win detection
```

## Architecture Notes

- **Single GUI file** — both vending machine and scratch ticket screens are defined
  as node sets in `main.gui`, toggled via `gui.set_enabled()` in the script.
- **No external assets** — uses Defold's built-in system font; all visuals are
  coloured box/text nodes. No sprites or atlases required.
- **Ticket generation** — winning tickets guarantee exactly 3 matching symbols
  (placed at random positions). Losing tickets are re-rolled until no 3-match exists.
