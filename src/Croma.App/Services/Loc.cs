using System.Globalization;

namespace FolderHue.App.Services;

/// <summary>
/// Tiny two-language string table. FolderHue ships in English with a Spanish option;
/// the choice is persisted and every surface (window, tray, context menu) follows it.
/// </summary>
public static class Loc
{
    public static event Action? Changed;

    private static string _language = "en";

    public static string Language => _language;
    public static bool IsSpanish => _language == "es";

    public static void Initialize(string? language)
    {
        _language = language is { Length: > 0 } && language.StartsWith("es", StringComparison.OrdinalIgnoreCase)
            ? "es" : "en";
    }

    public static void Set(string language)
    {
        string normalized = language.StartsWith("es", StringComparison.OrdinalIgnoreCase) ? "es" : "en";
        if (normalized == _language) return;
        _language = normalized;
        var cfg = ConfigStore.Current;
        cfg.Language = normalized;
        ConfigStore.Save();
        Changed?.Invoke();
    }

    public static string T(string key)
    {
        if (!Table.TryGetValue(key, out var pair)) return key;
        return _language == "es" ? pair.Es : pair.En;
    }

    public static string F(string key, params object?[] args)
    {
        try { return string.Format(CultureInfo.CurrentCulture, T(key), args); }
        catch { return T(key); }
    }

    /// <summary>Localised display name of a palette colour.</summary>
    public static string ColorName(Core.FolderColor color) => IsSpanish ? color.NameEs : color.Name;

    private static readonly Dictionary<string, (string En, string Es)> Table = new()
    {
        // ── main window ────────────────────────────────────────────────
        ["PickColor"] = ("Pick a color", "Elige un color"),
        ["TagsTitle"] = ("Tags", "Etiquetas"),
        ["TagsHint"] = ("Rename a tag to find it later in Explorer", "Renombra una etiqueta para encontrarla luego en el Explorador"),
        ["ApplyGlobal"] = ("Apply to the whole system", "Aplicar a todo el sistema"),
        ["ApplyFolders"] = ("Apply to one or more folders…", "Aplicar a una o varias carpetas…"),
        ["DragHint"] = ("You can also drag folders here.", "También puedes arrastrar carpetas aquí."),
        ["StartWithWindows"] = ("Start with Windows", "Iniciar con Windows"),
        ["RestartExplorer"] = ("Restart Explorer when applying", "Reiniciar el Explorador al aplicar"),
        ["ApplyShortcuts"] = ("Apply to shortcuts", "Aplicar a accesos directos"),
        ["RestoreOriginal"] = ("Restore the original yellow", "Restaurar el amarillo original"),
        ["RestoreConfirm"] = ("Restore every folder and the system to the original yellow?",
                              "¿Devolver todas las carpetas y el sistema al amarillo original?"),
        ["OriginalYellow"] = ("Original (yellow)", "Original (amarillo)"),
        ["OriginalDesc"] = ("The classic Windows yellow, untouched.", "El amarillo de siempre, sin cambios en el sistema."),
        ["ColorsCount"] = ("{0} colors", "{0} colores"),
        ["ChooseFolders"] = ("Choose one or more folders", "Elige una o varias carpetas"),
        ["TagRenameTip"] = ("Rename this tag: every folder using it updates in Explorer.",
                            "Renombra esta etiqueta: se actualizan todas sus carpetas en el Explorador."),
        ["NoTagsTitle"] = ("No tags yet.", "Todavía no hay etiquetas."),
        ["NoTagsBody"] = ("Pick a color and apply it to a folder: every color becomes a tag you can rename and open as a real folder in Explorer.",
                          "Elige un color y aplícalo a alguna carpeta: cada color se convierte en una etiqueta que puedes renombrar y abrir como carpeta en el Explorador."),
        ["Open"] = ("Open", "Abrir"),
        ["MsgPinManual"] = ("Windows blocked the automatic pin: right-click the selected shortcut and choose “Pin to taskbar”.",
                            "Windows bloqueó el anclaje automático: clic derecho en el acceso seleccionado y elige «Anclar a la barra de tareas»."),

        // ── picker popup ───────────────────────────────────────────────
        ["FolderColor"] = ("Folder color", "Color de carpeta"),
        ["BackToYellow"] = ("Back to yellow", "Devolver al amarillo"),
        ["OpenFolderHue"] = ("Open Croma", "Abrir Croma"),
        ["NoFolder"] = ("No folder", "Sin carpeta"),
        ["FoldersSelected"] = ("{0} folders selected", "{0} carpetas seleccionadas"),
        ["FolderColors"] = ("Folder colors", "Color de las carpetas"),

        // ── tray ───────────────────────────────────────────────────────
        ["CustomFolders"] = ("Custom folders ({0})", "Carpetas personalizadas ({0})"),
        ["ApplyToFolder"] = ("Apply color to a folder…", "Aplicar color a una carpeta…"),
        ["Options"] = ("Options", "Opciones"),
        ["TrayNotifications"] = ("Tray notifications", "Avisos de la bandeja"),
        ["RestoreAll"] = ("Restore everything to the original yellow", "Restaurar todo al amarillo original"),
        ["Exit"] = ("Exit", "Salir"),
        ["ChooseFolderToRecolor"] = ("Choose the folder to recolor", "Elige la carpeta a la que cambiar el color"),
        ["TrayTip"] = ("Croma — folder colors", "Croma — color de las carpetas"),

        // ── context menu ───────────────────────────────────────────────
        ["MenuFolder"] = ("Folder color", "Color de carpeta"),
        ["MenuThisFolder"] = ("Color this folder", "Color de esta carpeta"),
        ["MenuThisDrive"] = ("Color this drive", "Color de esta unidad"),
        ["MenuVisual"] = ("Visual picker…", "Selector visual…"),
        ["GroupBasics"] = ("Basics", "Básicos"),
        ["GroupWarm"] = ("Warm", "Cálidos"),
        ["GroupCool"] = ("Cool", "Fríos"),
        ["GroupNeutral"] = ("Neutral", "Neutros"),
        ["GroupSpecial"] = ("Special", "Especiales"),

        // ── messages ───────────────────────────────────────────────────
        ["MsgNoFolder"] = ("The folder no longer exists.", "La carpeta ya no existe."),
        ["MsgNoPermission"] = ("No permission to write in this folder.", "Sin permisos para escribir en esta carpeta."),
        ["MsgPainted"] = ("{0} is now {1}.", "{0} ahora es {1}."),
        ["MsgCouldNotChange"] = ("Could not change the color.", "No se pudo cambiar el color."),
        ["MsgRestored"] = ("{0} is back to the original yellow.", "{0} vuelve al amarillo original."),
        ["MsgCouldNotRestore"] = ("Could not restore the color.", "No se pudo restaurar el color."),
        ["MsgSystemApplied"] = ("{0} applied to the whole system.", "Color {0} aplicado a todo el sistema."),
        ["MsgCouldNotApply"] = ("Could not apply the color.", "No se pudo aplicar el color."),
        ["MsgUacCancelled"] = ("You cancelled the Windows prompt: nothing changed.", "Cancelaste el aviso de Windows: no se cambió nada."),
        ["MsgElevateFailed"] = ("Could not start the elevated process.", "No se pudo iniciar el proceso con permisos de administrador."),
        ["MsgWindowsError"] = ("Windows returned an error while applying the color.", "Windows devolvió un error al aplicar el color."),
        ["MsgTimeout"] = ("The elevated process did not respond in time.", "El proceso elevado no respondió a tiempo."),
        ["MsgApplying"] = ("Applying {0} to the whole system…", "Aplicando {0} a todo el sistema…"),
        ["MsgRestoringEverything"] = ("Restoring the original yellow…", "Restaurando el amarillo original…"),
        ["MsgTagRenamed"] = ("Tag renamed to “{0}”.", "Etiqueta renombrada a «{0}»."),
        ["MsgFoldersPainted"] = ("{0} folders painted with “{1}”.", "{0} carpetas pintadas con «{1}»."),
        ["MsgFolderPainted"] = ("{0} painted with “{1}”.", "{0} pintada con «{1}»."),
        ["MsgAppliedPick"] = ("✓ {0}", "✓ {0}"),
        ["MsgAppliedMany"] = ("✓ {0} folders · {1}", "✓ {0} carpetas · {1}"),
        ["MsgDone"] = ("✓ Done", "✓ Hecho"),

        // ── misc ───────────────────────────────────────────────────────
        ["ErrorTitle"] = ("Croma", "Croma"),
        ["CouldNotStart"] = ("Croma could not start:", "Croma no pudo iniciarse:"),
        ["Language"] = ("Language", "Idioma"),
    };
}
