using System.Windows.Forms;

namespace IcarusProspectEditor.Services;

/// <summary>
/// Prospect JSON uses negative <see cref="IcarusSaveLib.ProspectInfo.ExpireTime"/> (e.g. -1) for worlds that never expire.
/// </summary>
internal static class ProspectExpireTimeUi
{
    /// <summary>
    /// Value to persist in <c>ProspectInfo.ExpireTime</c> from the metadata UI.
    /// </summary>
    internal static long ToPersistedSeconds(bool neverExpires, DateTime pickerLocalValue)
    {
        if (neverExpires)
        {
            return -1;
        }

        return new DateTimeOffset(pickerLocalValue).ToUnixTimeSeconds();
    }

    /// <summary>
    /// Binds the never-expires checkbox and date picker from loaded prospect JSON (Unix seconds, or negative for never-expire).
    /// </summary>
    internal static void ApplyPersistedToControl(long expireTimeFromFile, CheckBox neverExpires, DateTimePicker picker)
    {
        if (expireTimeFromFile < 0)
        {
            neverExpires.Checked = true;
            picker.Enabled = false;
            var safe = DateTime.Today;
            picker.Value = ClampToPickerRange(picker, safe);
        }
        else
        {
            neverExpires.Checked = false;
            picker.Enabled = true;
            var dt = DateTimeOffset.FromUnixTimeSeconds(expireTimeFromFile).LocalDateTime;
            picker.Value = ClampToPickerRange(picker, dt);
        }
    }

    private static DateTime ClampToPickerRange(DateTimePicker picker, DateTime value)
    {
        if (value < picker.MinDate)
        {
            return picker.MinDate;
        }

        if (value > picker.MaxDate)
        {
            return picker.MaxDate;
        }

        return value;
    }
}
