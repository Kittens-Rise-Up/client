﻿namespace KRU.Networking
{
    public enum ClientPacketOpcode
    {
        Disconnect,
        PurchaseItem,
        CreateAccount,
        Login
    }

    public enum ServerPacketOpcode
    {
        ClientDisconnected,
        PurchasedItem,
        CreatedAccount,
        LoginResponse
    }

    public enum PurchaseItemResponseOpcode
    {
        Purchased,
        NotEnoughGold
    }

    public enum LoginResponseOpcode
    {
        LoginSuccess,
        VersionMismatch
    }

    public enum DisconnectOpcode 
    {
        Disconnected,
        Maintenance,
        Restarting,
        Kicked,
        Banned
    }

    public enum ItemType
    {
        Hut,
        Farm
    }
}
