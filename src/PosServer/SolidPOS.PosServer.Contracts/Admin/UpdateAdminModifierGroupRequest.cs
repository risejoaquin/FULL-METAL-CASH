namespace SolidPOS.PosServer.Contracts.Admin;

public sealed record UpdateAdminModifierGroupRequest(
    string Name,
    int MinSelected,
    int MaxSelected,
    bool Required);
