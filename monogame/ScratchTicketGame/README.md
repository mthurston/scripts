# Lucky Scratch — Ticket Vending Machine

A MonoGame proof-of-concept scratch-off lottery ticket vending machine.

## Gameplay

- Start with **$10.00** in your wallet
- Buy scratch tickets for **$1.00** each
- Scratch the 3×3 grid to reveal symbols
- **Match 3 of any symbol** to win **$5.00**
- See how long you can keep your balance alive!

## Controls

| Input | Action |
|-------|--------|
| `SPACE` | Buy a ticket / Return to vending machine |
| Click | Scratch an individual cell |
| `S` | Scratch all cells at once |
| `ESC` | Quit |

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [MonoGame](https://monogame.net/articles/getting_started/index.html) (installed via NuGet — no manual install needed)
- **Arial font** on the build machine
  - Windows: included by default
  - Linux: `sudo apt-get install ttf-mscorefonts-installer`
  - macOS: included by default

## Building & Running

```bash
cd monogame/ScratchTicketGame
dotnet run
```

Or to build only:

```bash
dotnet build
```

## Project Structure

```
ScratchTicketGame/
├── Game1.cs                    # Main game loop, scene management
├── Program.cs                  # Entry point
├── Models/
│   ├── Player.cs               # Balance, win/loss tracking
│   └── Ticket.cs               # Scratch ticket generation & win logic
├── Scenes/
│   ├── IScene.cs               # Scene interface
│   ├── VendingMachineScene.cs  # Ticket purchase screen
│   └── ScratchTicketScene.cs   # Scratch-off gameplay screen
└── Content/
    ├── Content.mgcb            # MonoGame content pipeline config
    └── Fonts/
        └── GameFont.spritefont # Font definition (Arial 16pt)
```

## Win Odds

Each ticket has a **30% chance** of being a winner, pre-determined at generation time.
The result is baked into the cell layout — winning tickets are guaranteed to contain exactly 3 matching symbols.
