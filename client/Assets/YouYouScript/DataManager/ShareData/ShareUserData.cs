using System;

/// <summary>账号与角色的 C# 会话数据，可直接供 HybridCLR 热更新逻辑访问。</summary>
public class ShareUserData : IDisposable
{
    public long AccountId { get; set; }
    public long CurrRoleId { get; set; }
    public int CurrJobId { get; set; }
    public void Dispose() { AccountId = 0; CurrRoleId = 0; CurrJobId = 0; }
}
