using ApexPost.Application;
using ApexPost.Domain;
using Moq;
using Xunit;

namespace ApexPost.UnitTests.Application;

// Copilot-assisted: first draft generated with GitHub Copilot (COPILOT-LOG.md, prompt 1), then reviewed and corrected.
public sealed class BranchAdministrationServiceTests
{
    private readonly Mock<IBranchRepository> _branches = new(MockBehavior.Strict);
    private readonly Mock<IUnitOfWork> _unitOfWork = new(MockBehavior.Strict);
    private readonly BranchAdministrationService _service;

    public BranchAdministrationServiceTests()
    {
        _unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _service = new BranchAdministrationService(_branches.Object, _unitOfWork.Object);
    }

    [Fact]
    public async Task CreateAsync_NewCode_StagesNormalisedBranchAndCommitsOnce()
    {
        // Arrange
        _branches.Setup(x => x.GetByCodeAsync("IND09", It.IsAny<CancellationToken>())).ReturnsAsync((Branch?)null);
        _branches.Setup(x => x.AddAsync(It.IsAny<Branch>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var command = new SaveBranchCommand(" ind09 ", " Indore West ", " Central ", "09:00-18:00", " Counter A ");

        // Act
        var branch = await _service.CreateAsync(command);

        // Assert
        Assert.Equal("IND09", branch.Code);
        Assert.Equal(BranchStatus.Active, branch.Status);
        _branches.Verify(x => x.AddAsync(
            It.Is<Branch>(b => b.Code == "IND09" && b.Name == "Indore West" && b.Region == "Central"
                               && b.Counters.Count == 1 && b.Counters[0].Name == "Counter A"),
            It.IsAny<CancellationToken>()), Times.Once());
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task CreateAsync_DuplicateCode_ThrowsDomainExceptionAndNeverCommits()
    {
        // Arrange
        _branches.Setup(x => x.GetByCodeAsync("IND01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Branch { Code = "IND01", Name = "Indore" });
        var command = new SaveBranchCommand("IND01", "Copy", "Central", "09:00-18:00", "Counter 1");

        // Act
        var act = () => _service.CreateAsync(command);

        // Assert
        var error = await Assert.ThrowsAsync<DomainException>(act);
        Assert.Equal("Branch code already exists.", error.Message);
        _branches.Verify(x => x.AddAsync(It.IsAny<Branch>(), It.IsAny<CancellationToken>()), Times.Never());
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsNullAndNeverCommits()
    {
        // Arrange
        _branches.Setup(x => x.GetByIdAsync("missing", It.IsAny<CancellationToken>())).ReturnsAsync((Branch?)null);

        // Act
        var result = await _service.UpdateAsync("missing", new SaveBranchCommand("IND01", "Name", "Region", "09:00-18:00", "Counter 1"));

        // Assert
        Assert.Null(result);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task UpdateAsync_ExistingBranch_RenamesFirstCounterAndKeepsCode()
    {
        // Arrange
        var existing = new Branch { Id = "branch-1", Code = "IND01", Name = "Old", Region = "Old", Counters = [new Counter("c-1", "Counter 1")] };
        _branches.Setup(x => x.GetByIdAsync("branch-1", It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _branches.Setup(x => x.UpdateAsync(existing, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // Act
        var result = await _service.UpdateAsync("branch-1", new SaveBranchCommand("IGNORED", "Indore Main", "Central", "08:00-20:00", "Front desk"));

        // Assert
        Assert.NotNull(result);
        Assert.Equal("IND01", result.Code); // the code is the business key and is never changed by an edit
        Assert.Equal("Indore Main", result.Name);
        Assert.Equal("Front desk", Assert.Single(result.Counters).Name);
        Assert.Equal("c-1", result.Counters[0].Id);
        _branches.Verify(x => x.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once());
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
    }
}
