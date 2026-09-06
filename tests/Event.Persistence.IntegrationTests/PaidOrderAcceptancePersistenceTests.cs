// ABOUTME: Verifies paid-order acceptance persistence is tenant-qualified, immutable, and optional only for historical attempts.
// ABOUTME: Guards the generated model against synthetic acceptance backfills and cross-tenant payment references.

using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests;

public sealed class PaidOrderAcceptancePersistenceTests
{
    [Test]
    public async Task ModelKeepsHistoricalAcceptanceNullableAndUsesTenantQualifiedForeignKeys()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite("Data Source=:memory:")
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var context = new ExploreDbContext(options);
        IModel model = context.GetService<IDesignTimeModel>().Model;
        IEntityType attempt = model.FindEntityType(typeof(PaymentAttempt))!;
        IEntityType acceptance = model.FindEntityType(typeof(PaidOrderAcceptanceSnapshot))!;
        IEntityType acceptanceLine = model.FindEntityType(typeof(PaidOrderAcceptanceLine))!;
        IEntityType saleControl = model.FindEntityType(typeof(PaidCheckoutSaleControl))!;
        IEntityType saleAudit = model.FindEntityType(typeof(PaidCheckoutSaleControlAudit))!;
        IEntityType review = model.FindEntityType(typeof(PaidCheckoutReviewApproval))!;

        await Assert.That(attempt.FindProperty(nameof(PaymentAttempt.PaidOrderAcceptanceSnapshotId))!.IsNullable).IsTrue();
        await Assert.That(attempt.GetForeignKeys().Any(foreignKey =>
            foreignKey.PrincipalEntityType == acceptance &&
            foreignKey.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(PaymentAttempt.TenantId), nameof(PaymentAttempt.PaidOrderAcceptanceSnapshotId)]))).IsTrue();
        await Assert.That(attempt.GetIndexes().Any(index => !index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(
            [nameof(PaymentAttempt.TenantId), nameof(PaymentAttempt.PaidOrderAcceptanceSnapshotId)]))).IsTrue();
        await Assert.That(acceptance.FindProperty("LineFactsJson")).IsNull();
        await Assert.That(acceptance.FindProperty(nameof(PaidOrderAcceptanceSnapshot.OrganizerActorId))!.IsNullable).IsFalse();
        await Assert.That(acceptance.FindProperty(nameof(PaidOrderAcceptanceSnapshot.TenantDirectoryOperatorDocumentId))!.IsNullable).IsFalse();
        await Assert.That(acceptance.FindProperty(nameof(PaidOrderAcceptanceSnapshot.TenantDirectoryOperatorRevisionId))!.IsNullable).IsFalse();
        await Assert.That(acceptance.FindProperty(nameof(PaidOrderAcceptanceSnapshot.AcceptanceTemplateIdentifier))!.GetMaxLength()).IsEqualTo(80);
        await Assert.That(acceptance.FindProperty(nameof(PaidOrderAcceptanceSnapshot.TenantDirectoryOperatorLegalName))!.GetMaxLength()).IsEqualTo(300);
        await Assert.That(acceptance.FindProperty(nameof(PaidOrderAcceptanceSnapshot.OrganizerPaymentProviderConnectionId))!.IsNullable).IsFalse();
        await Assert.That(acceptance.FindProperty(nameof(PaidOrderAcceptanceSnapshot.ConnectPlatformId))!.GetMaxLength()).IsEqualTo(120);
        await Assert.That(acceptance.FindProperty(nameof(PaidOrderAcceptanceSnapshot.ExternalAccountId))!.GetMaxLength()).IsEqualTo(200);
        await Assert.That(acceptance.FindProperty(nameof(PaidOrderAcceptanceSnapshot.MerchantCountryCode))!.GetMaxLength()).IsEqualTo(2);
        await Assert.That(acceptance.FindProperty(nameof(PaidOrderAcceptanceSnapshot.OperatorLegalName))!.GetMaxLength()).IsEqualTo(300);
        await Assert.That(acceptance.FindProperty(nameof(PaidOrderAcceptanceSnapshot.OperatorKindCode))!.GetMaxLength()).IsEqualTo(80);
        await Assert.That(acceptance.FindProperty(nameof(PaidOrderAcceptanceSnapshot.OperatorRegistrationIdentifier))!.GetMaxLength()).IsEqualTo(120);
        await Assert.That(acceptanceLine.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(acceptanceLine.GetForeignKeys().Any(foreignKey =>
            foreignKey.PrincipalEntityType == acceptance &&
            foreignKey.DeleteBehavior == DeleteBehavior.Restrict &&
            foreignKey.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(PaidOrderAcceptanceLine.TenantId), nameof(PaidOrderAcceptanceLine.PaidOrderAcceptanceSnapshotId)]))).IsTrue();
        await Assert.That(acceptanceLine.GetCheckConstraints().Select(constraint => constraint.Name)).Contains("ck_paid_order_acceptance_lines_shape");
        await Assert.That(saleControl.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(saleAudit.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(review.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(saleAudit.GetForeignKeys().Any(foreignKey => foreignKey.PrincipalEntityType == saleControl &&
            foreignKey.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(PaidCheckoutSaleControlAudit.TenantId), nameof(PaidCheckoutSaleControlAudit.PaidCheckoutSaleControlId)]))).IsTrue();
        await Assert.That(review.GetCheckConstraints().Select(constraint => constraint.Name)).Contains("ck_paid_checkout_review_approvals_separation");
        await Assert.That(acceptance.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(acceptance.GetIndexes().Any(index => index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(
            [nameof(PaidOrderAcceptanceSnapshot.TenantId), nameof(PaidOrderAcceptanceSnapshot.RegistrationOrderId), nameof(PaidOrderAcceptanceSnapshot.DisclosureRevision)]))).IsTrue();
    }
}
