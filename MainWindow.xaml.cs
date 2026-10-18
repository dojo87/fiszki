using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace IrregularVerbTrainer;

public partial class MainWindow : Window
{
    private readonly Random _random = new();
    private string _dataPath = string.Empty;
    private List<string> _headers = new();
    private List<VerbEntry> _verbs = new();
    private List<TextBox> _inputs = new();
    private VerbEntry? _currentVerb;
    private int _promptColumn;

    public MainWindow()
    {
        InitializeComponent();

        try
        {
            _dataPath = ResolveDataPath(Environment.GetCommandLineArgs().Skip(1));
            ApplyData(_dataPath, ReadVerbs(_dataPath));
            BuildForm();
            ShowNextQuestion();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Could not start the trainer.\n\n{exception.Message}",
                "Irregular Verb Trainer", MessageBoxButton.OK, MessageBoxImage.Error);
            CheckButton.IsEnabled = false;
            NextButton.IsEnabled = false;
        }
    }

    private static string ResolveDataPath(IEnumerable<string> arguments)
    {
        var args = arguments.ToArray();
        string? source = null;
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index].Equals("--source", StringComparison.OrdinalIgnoreCase))
            {
                if (source is not null || index + 1 >= args.Length || args[index + 1].StartsWith("--"))
                    throw new ArgumentException("Usage: IrregularVerbTrainer.exe [--source <csv-path>]");
                source = args[++index];
            }
            else if (args[index].StartsWith("--source=", StringComparison.OrdinalIgnoreCase))
            {
                if (source is not null || args[index].Length == "--source=".Length)
                    throw new ArgumentException("Usage: IrregularVerbTrainer.exe [--source <csv-path>]");
                source = args[index]["--source=".Length..];
            }
            else
            {
                throw new ArgumentException($"Unknown argument: {args[index]}");
            }
        }

        if (source is not null)
            return Path.GetFullPath(source);

        var lastLoadedPath = GetLastLoadedPath();
        if (lastLoadedPath is not null && File.Exists(lastLoadedPath))
            return Path.GetFullPath(lastLoadedPath);

        return GetDefaultDataPath();
    }

    private static string GetDefaultDataPath()
    {
        var defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IrregularVerbTrainer",
            "irregular-verbs.csv");
        if (!File.Exists(defaultPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(defaultPath)!);
            var bundledCsv = Path.Combine(AppContext.BaseDirectory, "irregular-verbs.csv");
            File.Copy(bundledCsv, defaultPath);
        }

        return defaultPath;
    }

    private static string GetSettingsDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IrregularVerbTrainer");

    private static string GetLastLoadedPathFile() =>
        Path.Combine(GetSettingsDirectory(), "last-csv-path.txt");

    private static string? GetLastLoadedPath()
    {
        var pathFile = GetLastLoadedPathFile();
        if (!File.Exists(pathFile))
            return null;

        var path = File.ReadAllText(pathFile, Encoding.UTF8).Trim();
        return string.IsNullOrEmpty(path) ? null : path;
    }

    private static void SaveLastLoadedPath(string path)
    {
        var settingsDirectory = GetSettingsDirectory();
        Directory.CreateDirectory(settingsDirectory);
        var pathFile = GetLastLoadedPathFile();
        var temporaryPath = pathFile + ".tmp";
        File.WriteAllText(temporaryPath, Path.GetFullPath(path),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, pathFile, overwrite: true);
    }

    private static VerbData ReadVerbs(string dataPath)
    {
        if (!File.Exists(dataPath))
            throw new FileNotFoundException("The specified CSV file was not found.", dataPath);

        var lines = File.ReadAllLines(dataPath, Encoding.UTF8);
        if (lines.Length < 2)
            throw new InvalidDataException("The CSV must contain a header and at least one verb.");

        var headers = ParseCsvLine(lines[0]);
        if (headers.Count == 0 || headers.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("The CSV needs at least one data column.");
        var hasWeightColumn = headers[^1].Trim().Equals("weight", StringComparison.OrdinalIgnoreCase);
        if (headers.Any(header => header.Trim().Equals("weight", StringComparison.OrdinalIgnoreCase))
            && !hasWeightColumn)
            throw new InvalidDataException("The last CSV column must be named 'weight'.");
        if (hasWeightColumn && headers.Count < 2)
            throw new InvalidDataException("The CSV needs at least one data column and a final weight column.");

        var dataColumnCount = hasWeightColumn ? headers.Count - 1 : headers.Count;
        if (headers.Take(dataColumnCount).Distinct(StringComparer.OrdinalIgnoreCase).Count() != dataColumnCount)
            throw new InvalidDataException("Data column names must be unique.");
        if (!hasWeightColumn)
            headers.Add("weight");

        var entries = new List<VerbEntry>();
        for (var lineNumber = 1; lineNumber < lines.Length; lineNumber++)
        {
            if (string.IsNullOrWhiteSpace(lines[lineNumber]))
                continue;

            var fields = ParseCsvLine(lines[lineNumber]);
            var expectedColumnCount = dataColumnCount + (hasWeightColumn ? 1 : 0);
            if (fields.Count != expectedColumnCount)
                throw new InvalidDataException($"CSV line {lineNumber + 1} must have {expectedColumnCount} columns.");
            var weight = 1.0;
            if (hasWeightColumn
                && (!double.TryParse(fields[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out weight)
                    || !double.IsFinite(weight) || weight <= 0))
                throw new InvalidDataException($"CSV line {lineNumber + 1} has an invalid weight.");
            if (fields.Take(dataColumnCount).Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException($"CSV line {lineNumber + 1} has an empty data field.");

            entries.Add(new VerbEntry(fields.Take(dataColumnCount).ToArray(), weight));
        }

        if (entries.Count == 0)
            throw new InvalidDataException("The CSV does not contain any entries.");
        if (!double.IsFinite(entries.Sum(entry => entry.Weight)))
            throw new InvalidDataException("The combined CSV weights are too large.");
        return new VerbData(headers, entries);
    }

    private void ApplyData(string dataPath, VerbData data)
    {
        _dataPath = Path.GetFullPath(dataPath);
        _headers = data.Headers;
        _verbs = data.Entries;
        SourceText.Text = Path.GetFileName(_dataPath);
        SourceText.ToolTip = _dataPath;
    }

    private void BuildForm()
    {
        _inputs.Clear();
        FormPanel.Children.Clear();
        for (var column = 0; column < _headers.Count - 1; column++)
        {
            var label = new TextBlock
            {
                Text = _headers[column],
                Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, column == 0 ? 0 : 14, 0, 6)
            };
            var input = new TextBox { Margin = new Thickness(0, 0, 0, 0) };
            FormPanel.Children.Add(label);
            FormPanel.Children.Add(input);
            _inputs.Add(input);
        }
    }

    private void ShowNextQuestion()
    {
        var selection = _random.NextDouble() * _verbs.Sum(verb => verb.Weight);
        _currentVerb = _verbs[^1];
        foreach (var verb in _verbs)
        {
            selection -= verb.Weight;
            if (selection < 0)
            {
                _currentVerb = verb;
                break;
            }
        }

        _promptColumn = _random.Next(_headers.Count - 1);
        for (var column = 0; column < _inputs.Count; column++)
        {
            var isPrompt = column == _promptColumn;
            _inputs[column].Text = isPrompt ? _currentVerb.Values[column] : "";
            _inputs[column].IsReadOnly = isPrompt;
            _inputs[column].IsEnabled = true;
            _inputs[column].Background = isPrompt
                ? new SolidColorBrush(Color.FromRgb(238, 244, 255))
                : Brushes.White;
            _inputs[column].Foreground = isPrompt
                ? new SolidColorBrush(Color.FromRgb(23, 59, 115))
                : Brushes.Black;
            _inputs[column].FontWeight = isPrompt ? FontWeights.Bold : FontWeights.Normal;
        }

        FeedbackText.Text = "";
        FeedbackBorder.Visibility = Visibility.Collapsed;
        CheckButton.IsEnabled = true;
        NextButton.IsEnabled = false;
        ProgressText.Text = $"Weighted practice · {_verbs.Count} entries";
        _inputs[(_promptColumn + 1) % _inputs.Count].Focus();
    }

    private void CheckButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentVerb is null)
            return;

        var incorrectColumns = Enumerable.Range(0, _inputs.Count)
            .Where(column => column != _promptColumn
                && !string.Equals(_inputs[column].Text.Trim(), _currentVerb.Values[column],
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
        var answerableColumns = _inputs.Count - 1;
        var weightChange = 0.5 * incorrectColumns.Count / answerableColumns;
        var originalWeight = _currentVerb.Weight;
        _currentVerb.Weight = incorrectColumns.Count == 0
            ? Math.Max(1, originalWeight - 0.5)
            : originalWeight + weightChange;

        try
        {
            SaveVerbs();
        }
        catch (Exception exception)
        {
            _currentVerb.Weight = originalWeight;
            MessageBox.Show(this, $"Could not save the updated weight.\n\n{exception.Message}",
                "Irregular Verb Trainer", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (incorrectColumns.Count == 0)
        {
            ShowFeedback("Correct! All fields are right.", isCorrect: true);
        }
        else
        {
            var incorrectNames = string.Join(", ", incorrectColumns.Select(column => _headers[column]));
            var answers = string.Join(", ", incorrectColumns.Select(column =>
                $"{_headers[column]}: {_currentVerb.Values[column]}"));
            ShowFeedback($"Incorrect: {incorrectNames}. Correct answers: {answers}", isCorrect: false);
        }

        CheckButton.IsEnabled = false;
        NextButton.IsEnabled = true;
        foreach (var input in _inputs)
            input.IsEnabled = false;
    }

    private void ShowFeedback(string message, bool isCorrect)
    {
        FeedbackText.Text = message;
        FeedbackText.Foreground = new SolidColorBrush(isCorrect
            ? Color.FromRgb(22, 101, 52)
            : Color.FromRgb(185, 28, 28));
        FeedbackBorder.Background = new SolidColorBrush(isCorrect
            ? Color.FromRgb(220, 252, 231)
            : Color.FromRgb(254, 226, 226));
        FeedbackBorder.Visibility = Visibility.Visible;
    }

    private void SaveVerbs()
    {
        var contents = new StringBuilder();
        contents.AppendLine(string.Join(",", _headers.Select(EscapeCsv)));
        foreach (var verb in _verbs)
        {
            contents.Append(string.Join(",", verb.Values.Select(EscapeCsv)));
            contents.Append(',');
            contents.AppendLine(verb.Weight.ToString("R", CultureInfo.InvariantCulture));
        }

        var temporaryPath = _dataPath + ".tmp";
        File.WriteAllText(temporaryPath, contents.ToString(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, _dataPath, overwrite: true);
    }

    private void NextButton_Click(object sender, RoutedEventArgs e) => ShowNextQuestion();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        if (CheckButton.IsEnabled)
            CheckButton_Click(CheckButton, new RoutedEventArgs());
        else if (NextButton.IsEnabled)
            NextButton_Click(NextButton, new RoutedEventArgs());
    }

    private void ChooseFileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a CSV file for training",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (Directory.Exists(Path.GetDirectoryName(_dataPath)))
            dialog.InitialDirectory = Path.GetDirectoryName(_dataPath);

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var data = ReadVerbs(dialog.FileName);
            SaveLastLoadedPath(dialog.FileName);
            ApplyData(dialog.FileName, data);
            BuildForm();
            ShowNextQuestion();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Could not load the selected CSV file.\n\n{exception.Message}",
                "Irregular Verb Trainer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string EscapeCsv(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == ',' && !quoted)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(character);
            }
        }

        if (quoted)
            throw new InvalidDataException("The CSV contains an unclosed quoted field.");
        fields.Add(field.ToString());
        return fields;
    }

    private sealed class VerbEntry
    {
        public VerbEntry(string[] values, double weight)
        {
            Values = values;
            Weight = weight;
        }

        public string[] Values { get; }
        public double Weight { get; set; }
    }

    private sealed record VerbData(List<string> Headers, List<VerbEntry> Entries);
}
