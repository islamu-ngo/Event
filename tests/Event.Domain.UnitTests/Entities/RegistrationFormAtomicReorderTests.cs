using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Domain.UnitTests.Entities;

public sealed class RegistrationFormAtomicReorderTests
{
    private static readonly DateTime Now = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task SingleSectionUpdate_CannotPerformAdjacentSwap()
    {
        var graph = BuildGraph();

        void Act() => graph.Version.UpdateSection(graph.FirstSection, 2, graph.FirstSection.Title);

        await Assert.That(Act).Throws<ArgumentException>();
        await Assert.That(graph.FirstSection.Ordinal).IsEqualTo(1);
        await Assert.That(graph.SecondSection.Ordinal).IsEqualTo(2);
    }

    [Test]
    public async Task ReorderSections_AtomicallySwapsCompleteMembershipAndAdvancesStampOnce()
    {
        var graph = BuildGraph();
        Guid before = graph.Version.ConcurrencyStamp;

        graph.Version.ReorderSections([graph.SecondSection.Id, graph.FirstSection.Id]);

        await Assert.That(graph.SecondSection.Ordinal).IsEqualTo(1);
        await Assert.That(graph.FirstSection.Ordinal).IsEqualTo(2);
        await Assert.That(graph.Version.ConcurrencyStamp).IsNotEqualTo(before);
    }

    [Test]
    public async Task ReorderFields_AtomicallySwapsCompleteSectionMembershipAndAdvancesStampOnce()
    {
        var graph = BuildGraph();
        Guid before = graph.Version.ConcurrencyStamp;

        graph.Version.ReorderFields(
            graph.FirstSection,
            [graph.SecondField.Id, graph.FirstField.Id]);

        await Assert.That(graph.SecondField.Ordinal).IsEqualTo(1);
        await Assert.That(graph.FirstField.Ordinal).IsEqualTo(2);
        await Assert.That(graph.Version.ConcurrencyStamp).IsNotEqualTo(before);
    }

    [Test]
    public async Task Reorder_RejectsDuplicateOmittedAndForeignIdsWithoutMutation()
    {
        var graph = BuildGraph();
        Guid before = graph.Version.ConcurrencyStamp;

        void DuplicateSections() => graph.Version.ReorderSections(
            [graph.FirstSection.Id, graph.FirstSection.Id]);
        void OmittedSections() => graph.Version.ReorderSections([graph.FirstSection.Id]);
        void ForeignFields() => graph.Version.ReorderFields(
            graph.FirstSection,
            [graph.FirstField.Id, Guid.CreateVersion7()]);

        await Assert.That(DuplicateSections).Throws<ArgumentException>();
        await Assert.That(OmittedSections).Throws<ArgumentException>();
        await Assert.That(ForeignFields).Throws<ArgumentException>();
        await Assert.That(graph.FirstSection.Ordinal).IsEqualTo(1);
        await Assert.That(graph.SecondSection.Ordinal).IsEqualTo(2);
        await Assert.That(graph.FirstField.Ordinal).IsEqualTo(1);
        await Assert.That(graph.SecondField.Ordinal).IsEqualTo(2);
        await Assert.That(graph.Version.ConcurrencyStamp).IsEqualTo(before);
    }

    private static Graph BuildGraph()
    {
        Guid tenantId = Guid.CreateVersion7();
        RegistrationForm form = RegistrationForm.Create(
            Guid.CreateVersion7(), tenantId, Guid.CreateVersion7(),
            "platform.registration", "attendee", "Attendee", Now);
        RegistrationFormVersion version = RegistrationFormVersion.Create(
            Guid.CreateVersion7(), form, 1, "en-US", null, null, Now);
        RegistrationFormSection firstSection = RegistrationFormSection.Create(
            Guid.CreateVersion7(), version, 1, "First", Now);
        RegistrationFormSection secondSection = RegistrationFormSection.Create(
            Guid.CreateVersion7(), version, 2, "Second", Now);
        version.AddSection(firstSection);
        version.AddSection(secondSection);

        RegistrationFormField firstField = RegistrationFormField.Create(
            Guid.CreateVersion7(), firstSection, 1, "platform.registration", "first",
            "First", RegistrationFieldTypeEnum.ShortText, 1,
            RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, false, false, Now);
        RegistrationFormField secondField = RegistrationFormField.Create(
            Guid.CreateVersion7(), firstSection, 2, "platform.registration", "second",
            "Second", RegistrationFieldTypeEnum.ShortText, 1,
            RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, false, false, Now);
        version.AddField(firstSection, firstField);
        version.AddField(firstSection, secondField);
        return new(version, firstSection, secondSection, firstField, secondField);
    }

    private sealed record Graph(
        RegistrationFormVersion Version,
        RegistrationFormSection FirstSection,
        RegistrationFormSection SecondSection,
        RegistrationFormField FirstField,
        RegistrationFormField SecondField);
}
