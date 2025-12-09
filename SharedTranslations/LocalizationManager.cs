namespace CSLModsCommon.Manager;

internal class LocalizationManager {
    public static string LocalizeFormat(string format, params object[] args) => string.Format(Localize(format), args);

    public static string LocalizeFormat(string format, object arg0, object arg1, object arg2) => string.Format(Localize(format), arg0, arg1, arg2);

    public static string LocalizeFormat(string format, object arg0, object arg1) => string.Format(Localize(format), arg0, arg1);

    public static string LocalizeFormat(string format, object arg0) => string.Format(Localize(format), arg0);

    public static string Localize(string key) => string.Empty;
}

