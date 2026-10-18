# Repository guidance

## Build and run

- Build the Windows WPF application with `dotnet build .\IrregularVerbTrainer.csproj`.
- Run it with `dotnet run --project .\IrregularVerbTrainer.csproj`.
- To use a specific data file, pass `-- --source <csv-path>` after the project command, for example `dotnet run --project .\IrregularVerbTrainer.csproj -- --source .\irregular-verbs.csv`.
- This repository currently has no test project or configured lint command.

## Architecture

This is a small .NET 10 Windows-only WPF application. `App.xaml` provides shared control styles and starts `MainWindow.xaml`; the window's code-behind (`MainWindow.xaml.cs`) owns the trainer workflow, builds the form dynamically, loads and validates the verb data, checks answers, and persists practice weights. Keep the XAML layout and event handlers in sync when changing the UI.

The form's data columns come from the CSV header rather than being hard-coded. By default, the app copies the bundled `irregular-verbs.csv` to `%LOCALAPPDATA%\IrregularVerbTrainer\irregular-verbs.csv` on first launch and uses that user copy thereafter. `--source` selects an explicit CSV path instead. The final CSV column must be named `weight`; each row's positive weight controls its chance of appearing, and an incorrect answer adds `0.5` to that entry's weight and saves it.

## Data and code conventions

- Keep CSV text fields before the final `weight` field; data headers must be non-empty and unique. Weights are parsed and written with invariant-culture numeric formatting.
- CSV parsing and escaping are implemented locally in `MainWindow.xaml.cs`; preserve support for commas and escaped double quotes when changing CSV handling.
- The application uses nullable reference types and implicit usings as configured in the project file. WPF types and UI updates belong in the existing window code-behind; shared visual defaults belong in `App.xaml`.
