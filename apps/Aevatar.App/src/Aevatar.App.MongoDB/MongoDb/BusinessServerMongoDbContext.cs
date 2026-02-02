using Aevatar.App.LanguageManagement;
using Aevatar.App.Terms;
using Volo.Abp.Data;
using Volo.Abp.MongoDB;
using MongoDB.Driver;

namespace Aevatar.App.MongoDB;

[ConnectionStringName("Default")]
public class AppMongoDbContext : AbpMongoDbContext
{

    /* Add mongo collections here. Example:
     * public IMongoCollection<Question> Questions => Collection<Question>();
     */
    
    public IMongoCollection<Language> Languages { get; private set; }
    public IMongoCollection<LanguageText> LanguageTexts { get; private set; }
    public IMongoCollection<TermsOfServiceVersion> TermsOfServiceVersions { get; private set; }
    public IMongoCollection<UserTermsConsent> UserTermsConsents { get; private set; }

    protected override void CreateModel(IMongoModelBuilder modelBuilder)
    {
        base.CreateModel(modelBuilder);
        
        modelBuilder.Entity<Language>(b => b.CollectionName = "Languages");
        modelBuilder.Entity<LanguageText>(b => b.CollectionName = "LanguageTexts");

        modelBuilder.Entity<TermsOfServiceVersion>(b => b.CollectionName = "TermsOfServiceVersions");
        modelBuilder.Entity<UserTermsConsent>(b => b.CollectionName = "UserTermsConsents");

        //builder.Entity<YourEntity>(b =>
        //{
        //    //...
        //});
    }
}
