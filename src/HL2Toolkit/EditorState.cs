namespace HL2Toolkit;

public enum Tool { Select, Move, Rotate, Scale, TerrainRaise, TerrainLower, TerrainSmooth, TerrainFlatten, TerrainPaint }

/// <summary>Общее состояние редактора: сцена, выделение, инструмент, история отмены.</summary>
public sealed class EditorState
{
    public Scene Scene = Scene.CreateDefault();
    public SceneObject Selected;
    public Tool Tool = Tool.Move;
    public float BrushRadius = 384, BrushStrength = 0.5f;
    public bool Snap = true;
    public float Grid = 16;
    public float AngleSnap = 15;
    public string ProjectPath;
    public bool Dirty;

    public event Action SceneChanged, SelectionChanged, ToolChanged;
    public event Action<bool> TerrainChanged;   // true = полностью (сброс размеров)

    readonly List<string> _undo = new(), _redo = new();
    const int MaxUndo = 80;

    public bool IsTerrainTool => Tool >= Tool.TerrainRaise;

    public void SetTool(Tool t) { Tool = t; ToolChanged?.Invoke(); }

    public void Select(SceneObject o)
    {
        if (Selected == o) return;
        Selected = o;
        SelectionChanged?.Invoke();
    }

    /// <summary>Снимок перед изменением (для Ctrl+Z).</summary>
    public void PushUndo()
    {
        _undo.Add(Scene.ToJson());
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        _redo.Clear();
        Dirty = true;
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Add(Scene.ToJson());
        Restore(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Add(Scene.ToJson());
        Restore(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
    }

    void Restore(string json)
    {
        int selIdx = Selected != null ? Scene.Objects.IndexOf(Selected) : -1;
        Scene = Scene.FromJson(json);
        Selected = selIdx >= 0 && selIdx < Scene.Objects.Count ? Scene.Objects[selIdx] : null;
        Dirty = true;
        TerrainChanged?.Invoke(true);
        SceneChanged?.Invoke();
        SelectionChanged?.Invoke();
    }

    public void LoadScene(Scene s, string path)
    {
        Scene = s; ProjectPath = path; Selected = null; Dirty = false;
        _undo.Clear(); _redo.Clear();
        TerrainChanged?.Invoke(true);
        SceneChanged?.Invoke();
        SelectionChanged?.Invoke();
    }

    public void NotifyChanged() { Dirty = true; SceneChanged?.Invoke(); }
    public void NotifyTerrain(bool full) { Dirty = true; TerrainChanged?.Invoke(full); }

    public void Add(SceneObject o)
    {
        PushUndo();
        Scene.Objects.Add(o);
        SceneChanged?.Invoke();
        Select(o);
    }

    public void DeleteSelected()
    {
        if (Selected == null) return;
        PushUndo();
        Scene.Objects.Remove(Selected);
        Selected = null;
        SceneChanged?.Invoke();
        SelectionChanged?.Invoke();
    }

    public void DuplicateSelected()
    {
        if (Selected == null) return;
        var c = Selected.Clone();
        c.Pos[0] += 64; c.Pos[1] += 64;
        Add(c);
    }

    public float SnapV(float v) => Snap ? MathF.Round(v / Grid) * Grid : v;
    public float SnapA(float v) => Snap ? MathF.Round(v / AngleSnap) * AngleSnap : v;
}
