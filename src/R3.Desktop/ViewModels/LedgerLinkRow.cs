namespace R3.Desktop.ViewModels;

/// <summary>
/// Ids a ledger/report row carries so its right-click menu can navigate to the related record
/// (StandardContextActions.LedgerLinks). Empty string = no such link on this row; the matching
/// action is hidden rather than shown disabled.
/// </summary>
public interface ILedgerLinkRow
{
    string AccountId { get; }
    string SourceType { get; }
    string SourceId { get; }
    string CashAccountId { get; }
    string BankAccountId { get; }
}
