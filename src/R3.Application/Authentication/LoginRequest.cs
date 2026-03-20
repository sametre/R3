namespace R3.Application.Authentication;

public sealed record LoginRequest(
    string StoreNumber,
    string UserName,
    string Pin);
