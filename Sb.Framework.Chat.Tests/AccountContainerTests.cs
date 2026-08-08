using Sb.Framework.Chat.Account;

namespace Sb.Framework.Chat.Tests;

public class AccountContainerTests : IDisposable
{
    public void Dispose()
    {
        foreach (var a in AccountContainer.GetAll())
            AccountContainer.Remove(a.AccountId);
    }

    [Fact]
    public void GetOrCreate_SameId_ReturnsSameBag()
    {
        var opts = new AccountOptions { AccountId = "a1", OwnerId = "user1", Type = AccountType.Personal };
        var first = AccountContainer.GetOrCreate(opts);
        var second = AccountContainer.GetOrCreate(opts);

        Assert.Same(first, second);
    }

    [Fact]
    public void Get_ReturnsBagOrNull()
    {
        AccountContainer.GetOrCreate(new AccountOptions { AccountId = "a1", Type = AccountType.Personal });

        Assert.NotNull(AccountContainer.Get("a1"));
        Assert.Null(AccountContainer.Get("nope"));
    }

    [Fact]
    public void GetByOwner_FiltersByOwner()
    {
        AccountContainer.GetOrCreate(new AccountOptions { AccountId = "a1", OwnerId = "u1", Type = AccountType.Personal });
        AccountContainer.GetOrCreate(new AccountOptions { AccountId = "a2", OwnerId = "u1", Type = AccountType.Personal });
        AccountContainer.GetOrCreate(new AccountOptions { AccountId = "a3", OwnerId = "u2", Type = AccountType.Personal });

        var owned = AccountContainer.GetByOwner("u1");
        Assert.Equal(2, owned.Count);
    }

    [Fact]
    public void UpdateState_RaisesEvent()
    {
        AccountContainer.GetOrCreate(new AccountOptions { AccountId = "a1", Type = AccountType.Personal });
        AccountStateChangedEventArgs? raised = null;
        AccountContainer.StateChanged += (_, e) => raised = e;

        AccountContainer.UpdateState("a1", AccountSessionState.Ready);

        Assert.NotNull(raised);
        Assert.Equal(AccountSessionState.Ready, raised!.NewState);
    }
}
