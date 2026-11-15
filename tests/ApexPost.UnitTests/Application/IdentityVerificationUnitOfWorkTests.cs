using ApexPost.Application;
using ApexPost.Domain;
using Moq;
using Xunit;

namespace ApexPost.UnitTests.Application;

/// <summary>One commit per use case: IUnitOfWork is mocked so the number of commits can be verified.</summary>
public sealed class IdentityVerificationUnitOfWorkTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly InMemoryRepository<IdentityVerification> _verifications = new();
    private readonly IdentityVerificationService _service;

    public IdentityVerificationUnitOfWorkTests()
    {
        var transaction = new CounterTransaction { Id = "tx-1", BranchCode = "IND01", Status = TransactionStatus.Open };
        var clerk = new Staff { Id = "staff-1", Name = "Clerk", Email = "clerk@test.local", Role = StaffRole.CounterClerk, HomeBranchCode = "IND01", Status = StaffStatus.Active };
        var branch = new Branch { Id = "branch-1", Code = "IND01", Name = "Indore", Region = "Central", Status = BranchStatus.Active };
        _unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _service = new IdentityVerificationService(_verifications, new InMemoryRepository<CounterTransaction>(transaction),
            new InMemoryRepository<Staff>(clerk), new InMemoryRepository<Branch>(branch), _unitOfWork.Object);
    }

    [Fact]
    public async Task RecordAsync_SuccessfulVerification_StoresMaskedNumberAndCommitsOnce()
    {
        // Arrange
        var command = new RecordIdentityVerificationCommand("tx-1", "staff-1", IdentityDocumentType.NationalId, "ABCD1234", VerificationResult.Successful);

        // Act
        var result = await _service.RecordAsync(command);

        // Assert
        Assert.Equal("****1234", result.MaskedDocumentNumber);
        Assert.Single(_verifications.Items);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task RecordAsync_PendingResult_ThrowsAndNeverCommits()
    {
        // Arrange
        var command = new RecordIdentityVerificationCommand("tx-1", "staff-1", IdentityDocumentType.NationalId, "ABCD1234", VerificationResult.Pending);

        // Act
        var act = () => _service.RecordAsync(command);

        // Assert
        await Assert.ThrowsAsync<DomainException>(act);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
    }
}
