# Contributing to OptiGames

Thanks for helping improve OptiGames. Because the app runs as administrator and
changes Windows configuration, correctness and reversibility matter more than
the number of tweaks it offers.

## Before opening a pull request

1. Open an issue for changes that alter Windows, driver, or power settings.
2. Keep the change focused; unrelated fixes should use separate pull requests.
3. Build the solution on Windows with `dotnet build OptiGames.sln -c Release`.
4. Test both the apply and revert paths on a machine you can safely restore.
5. Never include registry exports, driver databases, logs, or screenshots that
   contain personal data.

## Adding or changing a tweak

- Document every registry path, value name, type, on-state, and off-state.
- Use the genuine Windows default for the off-state. Do not infer it from one
  modified machine.
- Explain how the default was verified and which Windows versions were tested.
- Call out security, privacy, anti-cheat, update, or compatibility tradeoffs in
  the user-facing description.
- Make partial application detectable when a tweak writes multiple values.

## Pull requests

Describe what changed, why it is safe, how it was tested, and how a user can
reverse it. Screenshots are welcome for UI changes, but redact usernames,
machine names, account details, and other applications.

By contributing, you agree that your contribution is licensed under the MIT
License used by this repository.
