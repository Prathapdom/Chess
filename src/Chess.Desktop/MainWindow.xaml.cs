using System.Collections.ObjectModel;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chess.Domain;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Chess.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly AppWindow _appWindow;
    private readonly Button[,] _boardButtons = new Button[8, 8];
    private readonly ObservableCollection<string> _moveHistory = [];
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly DispatcherQueueTimer _clockTimer;
    private readonly DispatcherQueueTimer _onlinePollTimer;
    private ChessPosition _position = ChessPosition.CreateInitial();
    private Square? _selectedSquare;
    private GameStatus _status;
    private TimeSpan _whiteRemaining = TimeSpan.FromMinutes(5);
    private TimeSpan _blackRemaining = TimeSpan.FromMinutes(5);
    private TimeSpan _increment = TimeSpan.FromSeconds(3);
    private DateTimeOffset _turnStartedAtUtc = DateTimeOffset.UtcNow;
    private TimeSpan _serverClockOffset;
    private Guid? _onlineGameId;
    private bool _onlineMode;
    private bool _pollInFlight;

    public MainWindow()
    {
        InitializeComponent();
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
        _status = ChessRules.GetStatus(_position);
        MoveListView.ItemsSource = _moveHistory;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Resize(new SizeInt32(1280, 820));

        BuildBoard();
        _clockTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _clockTimer.Interval = TimeSpan.FromMilliseconds(250);
        _clockTimer.Tick += ClockTimer_Tick;
        _clockTimer.Start();

        _onlinePollTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _onlinePollTimer.Interval = TimeSpan.FromSeconds(1);
        _onlinePollTimer.Tick += OnlinePollTimer_Tick;

        Closed += (_, _) =>
        {
            _clockTimer.Stop();
            _onlinePollTimer.Stop();
            _httpClient.Dispose();
        };

        RefreshUi();
    }

    private void BuildBoard()
    {
        for (var row = 0; row < 8; row++)
        {
            BoardGrid.RowDefinitions.Add(new RowDefinition());
            BoardGrid.ColumnDefinitions.Add(new ColumnDefinition());
        }

        for (var displayRow = 0; displayRow < 8; displayRow++)
        {
            var rank = 7 - displayRow;
            for (var file = 0; file < 8; file++)
            {
                var square = new Square(file, rank);
                var button = new Button
                {
                    Tag = square,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0),
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    FontFamily = new FontFamily("Segoe UI Symbol"),
                    FontSize = 46
                };
                button.Click += BoardSquare_Click;
                Grid.SetRow(button, displayRow);
                Grid.SetColumn(button, file);
                BoardGrid.Children.Add(button);
                _boardButtons[file, rank] = button;
            }
        }
    }

    private void ClockTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_onlineMode)
        {
            RefreshUi(updateStatusOnly: false);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var (white, black) = GetEffectiveClocks(now);
        if (!_status.IsTerminal() && ((_position.SideToMove == PieceColor.White && white <= TimeSpan.Zero)
            || (_position.SideToMove == PieceColor.Black && black <= TimeSpan.Zero)))
        {
            CommitElapsed(now);
        }

        RefreshUi(updateStatusOnly: false);
    }

    private async void OnlinePollTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        await PollOnlineGameAsync();
    }

    private async void BoardSquare_Click(object sender, RoutedEventArgs args)
    {
        if (_status.IsTerminal() || sender is not Button { Tag: Square square })
        {
            return;
        }

        if (_onlineMode && _position.SideToMove != SelectedOnlineColor)
        {
            OnlineStatusText.Text = $"The server is waiting for {FormatColor(_position.SideToMove)}.";
            return;
        }

        var piece = _position[square];
        if (_selectedSquare is null)
        {
            if (piece is { Color: var color } && color == _position.SideToMove)
            {
                _selectedSquare = square;
                RefreshBoard();
            }

            return;
        }

        if (piece is { Color: var selectedColor } && selectedColor == _position.SideToMove)
        {
            _selectedSquare = square;
            RefreshBoard();
            return;
        }

        var candidates = ChessRules.GetLegalMoves(_position)
            .Where(move => move.From == _selectedSquare.Value && move.To == square)
            .ToArray();
        if (candidates.Length == 0)
        {
            _selectedSquare = null;
            RefreshBoard();
            return;
        }

        var move = candidates.FirstOrDefault(candidate => candidate.Promotion is null)
            is { } ordinaryMove
                ? ordinaryMove
                : candidates.First(candidate => candidate.Promotion == PieceType.Queen);

        if (_onlineMode)
        {
            await SubmitOnlineMoveAsync(move);
        }
        else
        {
            ApplyLocalMove(move);
        }
    }

    private void ApplyLocalMove(ChessMove move)
    {
        var now = DateTimeOffset.UtcNow;
        CommitElapsed(now);
        if (_status.IsTerminal())
        {
            RefreshUi();
            return;
        }

        if (!ChessRules.TryApplyMove(_position, move, out var next, out var applied))
        {
            return;
        }

        if (_position.SideToMove == PieceColor.White)
        {
            _whiteRemaining += _increment;
        }
        else
        {
            _blackRemaining += _increment;
        }

        _position = next;
        _moveHistory.Add(applied.ToUci());
        _status = ChessRules.GetStatus(_position, BuildPositionHistory());
        _selectedSquare = null;
        _turnStartedAtUtc = now;
        RefreshUi();
    }

    private async Task SubmitOnlineMoveAsync(ChessMove move)
    {
        if (_onlineGameId is not { } gameId || !TryGetServerUri(out var serverUri))
        {
            OnlineStatusText.Text = "Enter a valid API address first.";
            return;
        }

        try
        {
            var promotion = move.Promotion switch
            {
                PieceType.Queen => "q",
                PieceType.Rook => "r",
                PieceType.Bishop => "b",
                PieceType.Knight => "n",
                _ => null
            };
            var request = new MoveRequestDto(SelectedOnlineColor.ToString(), move.From.Name, move.To.Name, promotion);
            using var response = await _httpClient.PostAsJsonAsync(
                new Uri(serverUri, $"api/games/{gameId}/moves"),
                request,
                _jsonOptions);
            if (!response.IsSuccessStatusCode)
            {
                OnlineStatusText.Text = await ReadApiErrorAsync(response);
                _selectedSquare = null;
                RefreshBoard();
                return;
            }

            var snapshot = await response.Content.ReadFromJsonAsync<GameSnapshotDto>(_jsonOptions);
            if (snapshot is not null)
            {
                ApplyOnlineSnapshot(snapshot);
            }
        }
        catch (Exception ex)
        {
            OnlineStatusText.Text = $"Move failed: {ex.Message}";
        }
    }

    private void NewGame_Click(object sender, RoutedEventArgs args)
    {
        ResetToLocalGame();
    }

    private async void Resign_Click(object sender, RoutedEventArgs args)
    {
        if (_status.IsTerminal())
        {
            return;
        }

        if (!_onlineMode)
        {
            _status = _position.SideToMove == PieceColor.White ? GameStatus.WhiteResigned : GameStatus.BlackResigned;
            RefreshUi();
            return;
        }

        if (_onlineGameId is not { } gameId || !TryGetServerUri(out var serverUri))
        {
            OnlineStatusText.Text = "Enter a valid API address first.";
            return;
        }

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                new Uri(serverUri, $"api/games/{gameId}/resign"),
                new ResignRequestDto(SelectedOnlineColor.ToString()),
                _jsonOptions);
            if (!response.IsSuccessStatusCode)
            {
                OnlineStatusText.Text = await ReadApiErrorAsync(response);
                return;
            }

            var snapshot = await response.Content.ReadFromJsonAsync<GameSnapshotDto>(_jsonOptions);
            if (snapshot is not null)
            {
                ApplyOnlineSnapshot(snapshot);
            }
        }
        catch (Exception ex)
        {
            OnlineStatusText.Text = $"Resign failed: {ex.Message}";
        }
    }

    private async void CreateOnline_Click(object sender, RoutedEventArgs args)
    {
        if (!TryGetServerUri(out var serverUri))
        {
            OnlineStatusText.Text = "Enter a valid API address first.";
            return;
        }

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                new Uri(serverUri, "api/games"),
                new CreateGameRequestDto("Desktop White", "Desktop Black", 5, 3),
                _jsonOptions);
            response.EnsureSuccessStatusCode();
            var snapshot = await response.Content.ReadFromJsonAsync<GameSnapshotDto>(_jsonOptions)
                ?? throw new InvalidOperationException("The API returned an empty game.");
            GameIdBox.Text = snapshot.Id.ToString();
            _onlineMode = true;
            _onlineGameId = snapshot.Id;
            _onlinePollTimer.Start();
            ApplyOnlineSnapshot(snapshot);
            OnlineStatusText.Text = $"Game created. Share this id: {snapshot.Id}";
        }
        catch (Exception ex)
        {
            OnlineStatusText.Text = $"Could not create game: {ex.Message}";
        }
    }

    private async void LoadOnline_Click(object sender, RoutedEventArgs args)
    {
        if (!Guid.TryParse(GameIdBox.Text?.Trim(), out var gameId))
        {
            OnlineStatusText.Text = "Enter a valid game id.";
            return;
        }

        if (!TryGetServerUri(out var serverUri))
        {
            OnlineStatusText.Text = "Enter a valid API address first.";
            return;
        }

        try
        {
            using var response = await _httpClient.GetAsync(new Uri(serverUri, $"api/games/{gameId}"));
            response.EnsureSuccessStatusCode();
            var snapshot = await response.Content.ReadFromJsonAsync<GameSnapshotDto>(_jsonOptions)
                ?? throw new InvalidOperationException("The API returned an empty game.");
            _onlineMode = true;
            _onlineGameId = snapshot.Id;
            _onlinePollTimer.Start();
            ApplyOnlineSnapshot(snapshot);
            OnlineStatusText.Text = $"Loaded {snapshot.WhitePlayer} vs {snapshot.BlackPlayer}.";
        }
        catch (Exception ex)
        {
            OnlineStatusText.Text = $"Could not load game: {ex.Message}";
        }
    }

    private async Task PollOnlineGameAsync()
    {
        if (_pollInFlight || _onlineGameId is not { } gameId || !TryGetServerUri(out var serverUri))
        {
            return;
        }

        _pollInFlight = true;
        try
        {
            using var response = await _httpClient.GetAsync(new Uri(serverUri, $"api/games/{gameId}"));
            if (!response.IsSuccessStatusCode)
            {
                OnlineStatusText.Text = await ReadApiErrorAsync(response);
                return;
            }

            var snapshot = await response.Content.ReadFromJsonAsync<GameSnapshotDto>(_jsonOptions);
            if (snapshot is not null)
            {
                ApplyOnlineSnapshot(snapshot);
            }
        }
        catch (Exception ex)
        {
            OnlineStatusText.Text = $"Connection lost: {ex.Message}";
        }
        finally
        {
            _pollInFlight = false;
        }
    }

    private void ApplyOnlineSnapshot(GameSnapshotDto snapshot)
    {
        _position = ChessPosition.FromFen(snapshot.Fen);
        _status = snapshot.Status;
        _whiteRemaining = TimeSpan.FromMilliseconds(Math.Max(0, snapshot.WhiteClockMs));
        _blackRemaining = TimeSpan.FromMilliseconds(Math.Max(0, snapshot.BlackClockMs));
        _increment = TimeSpan.FromSeconds(snapshot.IncrementSeconds);
        _serverClockOffset = snapshot.ServerTimeUtc - DateTimeOffset.UtcNow;
        _turnStartedAtUtc = snapshot.ServerTimeUtc;
        _selectedSquare = null;
        _moveHistory.Clear();
        foreach (var move in snapshot.Moves)
        {
            _moveHistory.Add(move.Uci);
        }

        RefreshUi();
    }

    private void ResetToLocalGame()
    {
        _onlinePollTimer.Stop();
        _onlineMode = false;
        _onlineGameId = null;
        _serverClockOffset = TimeSpan.Zero;
        _position = ChessPosition.CreateInitial();
        _status = ChessRules.GetStatus(_position);
        _whiteRemaining = TimeSpan.FromMinutes(5);
        _blackRemaining = TimeSpan.FromMinutes(5);
        _increment = TimeSpan.FromSeconds(3);
        _turnStartedAtUtc = DateTimeOffset.UtcNow;
        _selectedSquare = null;
        _moveHistory.Clear();
        OnlineStatusText.Text = "Not connected";
        RefreshUi();
    }

    private void CommitElapsed(DateTimeOffset now)
    {
        if (_status.IsTerminal())
        {
            return;
        }

        var elapsed = now - _turnStartedAtUtc;
        if (elapsed > TimeSpan.Zero)
        {
            if (_position.SideToMove == PieceColor.White)
            {
                _whiteRemaining -= elapsed;
                if (_whiteRemaining <= TimeSpan.Zero)
                {
                    _whiteRemaining = TimeSpan.Zero;
                    _status = GameStatus.WhiteFlagged;
                }
            }
            else
            {
                _blackRemaining -= elapsed;
                if (_blackRemaining <= TimeSpan.Zero)
                {
                    _blackRemaining = TimeSpan.Zero;
                    _status = GameStatus.BlackFlagged;
                }
            }
        }

        _turnStartedAtUtc = now;
    }

    private (TimeSpan White, TimeSpan Black) GetEffectiveClocks(DateTimeOffset now)
    {
        var white = _whiteRemaining;
        var black = _blackRemaining;
        if (_status.IsTerminal())
        {
            return (white, black);
        }

        var elapsed = now - _turnStartedAtUtc;
        if (elapsed > TimeSpan.Zero)
        {
            if (_position.SideToMove == PieceColor.White)
            {
                white -= elapsed;
            }
            else
            {
                black -= elapsed;
            }
        }

        return (white, black);
    }

    private void RefreshUi(bool updateStatusOnly = false)
    {
        if (!updateStatusOnly)
        {
            RefreshBoard();
        }

        var now = _onlineMode ? DateTimeOffset.UtcNow + _serverClockOffset : DateTimeOffset.UtcNow;
        var (white, black) = GetEffectiveClocks(now);
        WhiteClockText.Text = FormatClock(white);
        BlackClockText.Text = FormatClock(black);
        StatusText.Text = FormatStatus();
    }

    private void RefreshBoard()
    {
        for (var rank = 0; rank < 8; rank++)
        {
            for (var file = 0; file < 8; file++)
            {
                var square = new Square(file, rank);
                var button = _boardButtons[file, rank];
                var piece = _position[square];
                button.Content = piece?.UnicodeSymbol ?? string.Empty;
                button.Background = square.IsLight
                    ? (Brush)Application.Current.Resources["LightSquareBrush"]
                    : (Brush)Application.Current.Resources["DarkSquareBrush"];
                button.Foreground = piece is { Color: PieceColor.White }
                    ? new SolidColorBrush(Colors.White)
                    : new SolidColorBrush(Colors.Black);
                button.BorderBrush = new SolidColorBrush(Colors.Transparent);
                button.BorderThickness = new Thickness(0);

                if (_selectedSquare == square)
                {
                    button.BorderBrush = (Brush)Application.Current.Resources["HighlightBrush"];
                    button.BorderThickness = new Thickness(4);
                }
                else if (_selectedSquare is { } selected
                    && ChessRules.GetLegalMoves(_position, selected).Any(move => move.To == square))
                {
                    button.BorderBrush = new SolidColorBrush(Colors.White);
                    button.BorderThickness = new Thickness(2);
                }
            }
        }
    }

    private string FormatStatus()
    {
        return _status switch
        {
            GameStatus.InProgress => $"{FormatColor(_position.SideToMove)} to move",
            GameStatus.Check => $"{FormatColor(_position.SideToMove)} is in check",
            GameStatus.Checkmate => $"Checkmate — {FormatColor(Opposite(_position.SideToMove))} wins",
            GameStatus.Stalemate => "Stalemate — draw",
            GameStatus.DrawFiftyMove => "Draw — fifty-move rule",
            GameStatus.DrawRepetition => "Draw — threefold repetition",
            GameStatus.DrawInsufficientMaterial => "Draw — insufficient material",
            GameStatus.WhiteFlagged => "Time — Black wins",
            GameStatus.BlackFlagged => "Time — White wins",
            GameStatus.WhiteResigned => "White resigned — Black wins",
            GameStatus.BlackResigned => "Black resigned — White wins",
            _ => _status.ToString()
        };
    }

    private static string FormatClock(TimeSpan value)
    {
        var milliseconds = Math.Max(0, (long)value.TotalMilliseconds);
        var minutes = milliseconds / 60000;
        var seconds = (milliseconds % 60000) / 1000;
        return $"{minutes:00}:{seconds:00}";
    }

    private static string FormatColor(PieceColor color) => color == PieceColor.White ? "White" : "Black";

    private PieceColor SelectedOnlineColor => OnlineColorBox.SelectedIndex == 1 ? PieceColor.Black : PieceColor.White;

    private static PieceColor Opposite(PieceColor color) => color == PieceColor.White ? PieceColor.Black : PieceColor.White;

    private List<string> BuildPositionHistory()
    {
        var history = new List<string> { ChessPosition.CreateInitial().PositionKey() };
        var position = ChessPosition.CreateInitial();
        foreach (var moveText in _moveHistory)
        {
            if (ChessMove.TryParseUci(moveText, out var move) && ChessRules.TryApplyMove(position, move, out var next, out _))
            {
                position = next;
                history.Add(position.PositionKey());
            }
        }

        return history;
    }

    private bool TryGetServerUri(out Uri serverUri)
    {
        var value = ServerUrlBox.Text?.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            serverUri = new Uri(parsed.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
            return true;
        }

        serverUri = new Uri("http://localhost:5180/");
        return false;
    }

    private static async Task<string> ReadApiErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                return error.GetString() ?? $"API error ({(int)response.StatusCode})";
            }
        }
        catch (JsonException)
        {
            // Fall through to the status code.
        }

        return $"API error ({(int)response.StatusCode})";
    }
}
