using System.Xml.Linq;
using Alpha6Ops.Accounts;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace Alpha6Ops.Server;

// Stores the data-protection key ring in PostgreSQL so hosts without a persistent disk (Render, containers)
// keep sign-in cookies valid across deploys and replicas. Keys are encrypted by the configured certificate
// before they reach this repository, so the table holds no usable secret on its own.
public sealed class DatabaseXmlRepository(IServiceScopeFactory scopes) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        return db.DataProtectionKeys.OrderBy(k => k.Id).Select(k => k.Xml).AsEnumerable().Select(XElement.Parse).ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        db.DataProtectionKeys.Add(new DataProtectionKey { FriendlyName = friendlyName ?? "", Xml = element.ToString(SaveOptions.DisableFormatting) });
        db.SaveChanges();
    }
}
