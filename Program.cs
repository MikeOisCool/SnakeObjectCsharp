using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

public static class Program
{
    public static void Main()
    {
        Console.CursorVisible = false;

        var game = new SnakeGame(
            width: 60,
            height: 20,
            highscoreFile: "highscore.txt"
        );

        game.Run();
    }
}

// ---------------------- GAME ----------------------

public class SnakeGame
{
    private readonly Random _random = new();
    private readonly HighscoreStore _store;

    private readonly int _startW;
    private readonly int _startH;

    // Spielfeld inkl. Rahmen-Koordinaten
    private int _width;
    private int _height;

    private int _left;
    private int _top;
    private int _right;
    private int _bottom;

    // Innenbereich (ohne Rahmen) – für Bounds
    private int _minX;
    private int _maxX;
    private int _minY;
    private int _maxY;

    private Snake _snake = null!;
    private List<Food> _foods = null!;
    private const int FoodCount = 3;

    private int _punkte;
    private int _highscore;
    private int _tickMs = 180;
    private ConsoleKey? _pendingKey;

    private bool _exit;
    private string _reason = "Game Over!";



    public SnakeGame(int width, int height, string highscoreFile)
    {
        _store = new HighscoreStore(highscoreFile);
        _highscore = _store.Load();

        _startW = Console.WindowWidth;
        _startH = Console.WindowHeight;

        // Falls Konsole kleiner: automatisch anpassen
        _width = Math.Min(width, Console.WindowWidth - 5);
        _height = Math.Min(height, Console.WindowHeight - 5);

        // Zentrieren
        _left = (Console.WindowWidth - _width) / 2;
        _top = (Console.WindowHeight - _height) / 2 - 1;

        _right = _left + _width - 1;
        _bottom = _top + _height - 1;

        // Innenbereich
        _minX = _left + 1;
        _maxX = _right - 1;
        _minY = _top + 1;
        _maxY = _bottom - 1;
    }

    public void Run()
    {
        do
        {
            ResetGame();
            DrawStaticUI();     // Rahmen einmal
            DrawInitialActors(); // Snake + Food einmal
            GameLoop();
            EndGameScreen();

        } while (AskPlayAgain());
    }

    private void ResetGame()
    {
        _exit = false;
        _punkte = 0;
        _tickMs = 180;
        _reason = "Game Over!";
        _pendingKey = null;

        int startX = _left + _width / 2;
        int startY = _top + _height / 2;

        _snake = new Snake(new List<(int x, int y)>
        {
            (startX, startY),
            (startX - 1, startY),
            (startX - 2, startY)
        });

        _foods = new List<Food>();

        for (int i = 0; i < FoodCount; i++)
        {
            var f = new Food();
            f.Place(_random, _minX, _maxX, _minY, _maxY, _snake.Segments, _foods);
            _foods.Add(f);
        }
    }

    private void GameLoop()
    {
        while (!_exit)
        {
            if (TerminalResized())
            {
                _reason = "Console resized. Program exiting.";
                _exit = true;
                break;
            }

            ReadInputNonBlocking();
            if (_exit) break;

            Tick();
            Thread.Sleep(_tickMs);
        }
    }

    private void Tick()
    {
        if (_pendingKey.HasValue)
        {
            _snake.TryChangeDirection(_pendingKey.Value);
            _pendingKey = null;
        }

        var head = _snake.Head;
        int newX = head.x + _snake.DirX;
        int newY = head.y + _snake.DirY;

        // Wand?
        if (newX < _minX || newX > _maxX || newY < _minY || newY > _maxY)
        {
            _reason = "Game Over: Du bist gegen die Wand gefahren!";
            _exit = true;
            return;
        }

        // Self-collision?
        Food? eatenFood = _foods.FirstOrDefault(f => f.X == newX && f.Y == newY);
        bool ate = eatenFood != null;
        var tail = _snake.Tail;

        bool hitsSelf = _snake.HitsSelf(newX, newY) && (ate || (newX, newY) != tail);
        if (hitsSelf)
        {
            _reason = "Game Over: Du bist in dich selbst gefahren!";
            _exit = true;
            return;
        }



        // Tail merken (für gezieltes Löschen)
        var tailBefore = tail;

        // Snake bewegen
        _snake.StepTo(newX, newY, grow: ate);

        // Rendering minimal:
        // 1) wenn NICHT gegessen: Tail löschen
        if (!ate)
        {
            EraseCell(tailBefore.x, tailBefore.y);
        }
        

        // 2) neuen Kopf zeichnen
        DrawCell(newX, newY, 'X');

        // 3) altes Kopf-Segment (das jetzt Body ist) als 'o' zeichnen
        //    (das ist das Segment, das vorher Kopf war)
        DrawCell(head.x, head.y, 'f');

        // 4) wenn gegessen: Score + neues Food
        if (ate)
        {
            _punkte += eatenFood!.Points;
            if (_punkte > _highscore) _highscore = _punkte;
            ShowScore();

            //Speed-Effekt (mit Grenzen)
            _tickMs += eatenFood.TickDelta;
            if (_tickMs < 60) _tickMs = 60;
            if (_tickMs > 300) _tickMs = 300;

            // Food neu platzieren + zeichnen
            eatenFood!.Place(_random, _minX, _maxX, _minY, _maxY, _snake.Segments, _foods);
            DrawCell(eatenFood.X, eatenFood.Y, eatenFood.Symbol);


        }
    }

    // ---------------- Input ----------------

    private void ReadInputNonBlocking()
    {
        while (Console.KeyAvailable)
        {
            var key = Console.ReadKey(true).Key;

            if (key == ConsoleKey.Escape)
            {
                _reason = "Abbruch (ESC).";
                _exit = true;
                return;
            }

            if (key == ConsoleKey.UpArrow || key == ConsoleKey.I ||
             key == ConsoleKey.DownArrow || key == ConsoleKey.K ||
             key == ConsoleKey.LeftArrow || key == ConsoleKey.J ||
             key == ConsoleKey.RightArrow || key == ConsoleKey.L) _pendingKey = key;
        }
    }

    // ---------------- Drawing (flackerfrei) ----------------

    private void DrawStaticUI()
    {
        // NICHT Console.Clear() pro Frame – aber beim Start ist ok:
        Console.Clear();
        DrawBorder();
        ShowScore();
    }

    private void DrawInitialActors()
    {
        // Food
        foreach (var f in _foods) DrawCell(f.X, f.Y, f.Symbol);
        // Snake
        for (int i = 0; i < _snake.Segments.Count; i++)
        {
            var (x, y) = _snake.Segments[i];
            DrawCell(x, y, i == 0 ? 'O' : 'o');
        }
    }

    private void DrawBorder()
    {
        for (int x = _left; x <= _right; x++)
        {
            Console.SetCursorPosition(x, _top);
            Console.Write('-');
            Console.SetCursorPosition(x, _bottom);
            Console.Write('-');
        }

        for (int y = _top; y <= _bottom; y++)
        {
            Console.SetCursorPosition(_left, y);
            Console.Write('|');
            Console.SetCursorPosition(_right, y);
            Console.Write('|');
        }

        Console.SetCursorPosition(_left, _top); Console.Write('+');
        Console.SetCursorPosition(_right, _top); Console.Write('+');
        Console.SetCursorPosition(_left, _bottom); Console.Write('+');
        Console.SetCursorPosition(_right, _bottom); Console.Write('+');
    }

    private void ShowScore()
    {
        Console.SetCursorPosition(_left, _bottom + 1);
        Console.Write($"Punkte: {_punkte}    Highscore: {_highscore}    ");
        Console.SetCursorPosition(_left, _bottom + 2);
        Console.Write("@=+1  $=+3  F=schneller  S=langsamer      ");

    }

    private void DrawCell(int x, int y, char c)
    {
        Console.SetCursorPosition(x, y);
        Console.Write(c);
    }

    private void EraseCell(int x, int y)
    {
        Console.SetCursorPosition(x, y);
        Console.Write(' ');
    }

    // ---------------- End / Replay ----------------

    private void EndGameScreen()
    {
        _store.Save(_highscore);

        int msgY = Math.Min(Console.WindowHeight - 1, _bottom + 3);
        Console.SetCursorPosition(_left, msgY);
        Console.Write($"{_reason}  Vielen dank fürs spielen! :)    ");
    }

    private bool AskPlayAgain()
    {
        int promptY = Math.Min(Console.WindowHeight - 1, _bottom + 4);
        Console.SetCursorPosition(_left, promptY);
        Console.Write("Nochmal spielen? (Y/N): ");

        while (true)
        {
            var key = Console.ReadKey(true).Key;
            if (key == ConsoleKey.Y) return true;
            if (key == ConsoleKey.N || key == ConsoleKey.Escape) return false;
        }
    }

    private bool TerminalResized() => Console.WindowWidth != _startW || Console.WindowHeight != _startH;
}

// ---------------------- SNAKE ----------------------

public class Snake
{
    public List<(int x, int y)> Segments { get; }
    public int DirX { get; private set; } = 1;
    public int DirY { get; private set; } = 0;

    public Snake(List<(int x, int y)> initialSegments)
    {
        Segments = initialSegments;
    }

    public (int x, int y) Head => Segments[0];
    public (int x, int y) Tail => Segments[^1];

    public bool HitsSelf(int x, int y)
    {
        // Treffer auf vorhandenes Segment (inkl. Tail) ist klassisch GameOver
        return Segments.Any(seg => seg.x == x && seg.y == y);
    }

    public void RemoveTail()
    {
        Segments.RemoveAt(Segments.Count - 1);
    }

    public void StepTo(int newX, int newY, bool grow)
    {

        Segments.Insert(0, (newX, newY));
        if (!grow)
            RemoveTail();


    }

    public void TryChangeDirection(ConsoleKey key)
    {
        // Keine 180°-Wende
        switch (key)
        {
            case ConsoleKey.UpArrow:
            case ConsoleKey.I:
                if (DirY != 1) { DirX = 0; DirY = -1; }
                break;
            case ConsoleKey.DownArrow:
            case ConsoleKey.K:
                if (DirY != -1) { DirX = 0; DirY = 1; }
                break;
            case ConsoleKey.LeftArrow:
            case ConsoleKey.J:
                if (DirX != 1) { DirX = -1; DirY = 0; }
                break;
            case ConsoleKey.RightArrow:
            case ConsoleKey.L:
                if (DirX != -1) { DirX = 1; DirY = 0; }
                break;
        }
    }
}

// ---------------------- FOOD ----------------------

public enum FoodType
{
    Normal, //@ +1 Punkt
    Bonus, //$ +3 Punkt
    Slow, // S +1 Punkt, Langsamer
    Fast // F +1 Punkt, schneller
}
public class Food
{
    public int X { get; private set; }
    public int Y { get; private set; }

    public FoodType Type { get; private set; } = FoodType.Normal;

    public char Symbol => Type switch
    {
        FoodType.Normal => '@',
        FoodType.Bonus => '$',
        FoodType.Slow => 'S',
        FoodType.Fast => 'F',
        _ => '@'
    };

    public int Points => Type switch
    {
        FoodType.Normal => 1,
        FoodType.Bonus => 3,
        FoodType.Slow => 1,
        FoodType.Fast => 1,
        _ => 1
    };

    public int TickDelta => Type switch
    {
        FoodType.Slow => +20,
        FoodType.Fast => -15,
        _ => 0
    };

    public void RerollType(Random random)
    {
        // Gewichtung: Normal häufig, Bonus/Fast,Slow seltener
        int r = random.Next(100);
        Type =
            r < 65 ? FoodType.Normal :
            r < 80 ? FoodType.Bonus :
            r < 90 ? FoodType.Fast :
                FoodType.Slow;
    }

    public void Place(
        Random random,
        int minX, int maxX,
        int minY, int maxY,
        List<(int x, int y)> snake,
        List<Food> foods)
    {
        RerollType(random);
        do
        {
            X = random.Next(minX, maxX + 1);
            Y = random.Next(minY, maxY + 1);
        }
        while (
                snake.Any(seg => seg.x == X && seg.y == Y) ||
                foods.Any(f => f != this && f.X == X && f.Y == Y)
                );
    }
}

// ---------------------- HIGHSCORE STORE ----------------------

public class HighscoreStore
{
    private readonly string _path;

    public HighscoreStore(string path) => _path = path;

    public int Load()
    {
        if (!File.Exists(_path)) return 0;
        var txt = File.ReadAllText(_path).Trim();
        return int.TryParse(txt, out int v) ? v : 0;
    }

    public void Save(int value)
    {
        File.WriteAllText(_path, value.ToString());
    }
}
