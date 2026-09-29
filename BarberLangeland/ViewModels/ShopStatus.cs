namespace BarberLangeland.ViewModels
{
    /// <summary>
    /// Whether the shop is open at a given moment, and when that next changes: the closing time
    /// while it is open, the next opening time while it is closed.
    /// </summary>
    public record ShopStatus(bool IsOpen, DateTime NextChange);
}
