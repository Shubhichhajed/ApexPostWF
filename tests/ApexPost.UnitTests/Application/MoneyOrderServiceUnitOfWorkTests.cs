using ApexPost.Application;
using ApexPost.Domain;
using Moq;
using Xunit;

namespace ApexPost.UnitTests.Application;

/// <summary>Money orders: one commit when issued, no commit when the identity rule fails.</summary>
public sealed class MoneyOrderServiceUnitOfWorkTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly InMemoryRepository<MoneyOrder> _orders = new();
    private readonly CounterTransaction _transaction = new() { Id = "tx-1", BranchCode = "IND01", CounterId = "IND01-C1", ClerkId = "clerk-1" };
    private readonly MoneyOrderService _service;

    public MoneyOrderServiceUnitOfWorkTests()
    {
        var clerk = new Staff { Id = "clerk-1", Name = "Demo Clerk", Email = "clerk@test.local", Role = StaffRole.CounterClerk, HomeBranchCode = "IND01", Status = StaffStatus.Active };
        var branch = new Branch { Id = "branch-1", Code = "IND01", Name = "Indore", Region = "Central", Status = BranchStatus.Active, Counters = [new Counter("IND01-C1", "Counter 1")] };
        _unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _service = new MoneyOrderService(
            _orders,
            new InMemoryRepository<IdentityVerification>(),
            new InMemoryRepository<Branch>(branch),
            new InMemoryRepository<Staff>(clerk),
            new InMemoryRepository<CounterTransaction>(_transaction),
            new StagingCounterService(_transaction),
            new MoneyOrderPolicy(1000m),
            _unitOfWork.Object);
    }

    [Fact]
    public async Task IssueAsync_BelowThresholdOnExistingBasket_AddsLineAndCommitsOnce()
    {
        // Arrange
        var command = new IssueMoneyOrderCommand(500m, "Demo Sender", "Demo Recipient", "IND01", "clerk-1", "tx-1", null, PaymentMethod.Cash);

        // Act
        var result = await _service.IssueAsync(command);

        // Assert
        Assert.Single(_orders.Items);
        Assert.Equal(_transaction.Id, result.CounterTransactionId);
        Assert.Equal(TransactionStatus.Open, _transaction.Status);
        Assert.Single(_transaction.Lines);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task IssueAsync_AboveThresholdWithoutVerification_ThrowsAndNeverCommits()
    {
        // Arrange
        var command = new IssueMoneyOrderCommand(1500m, "Demo Sender", "Demo Recipient", "IND01", "clerk-1", "tx-1", null, PaymentMethod.Cash);

        // Act
        var act = () => _service.IssueAsync(command);

        // Assert
        await Assert.ThrowsAsync<DomainException>(act);
        Assert.Empty(_orders.Items);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    private sealed class StagingCounterService(CounterTransaction transaction) : ICounterService
    {
        public Task<CounterTransaction> CreateAsync(string branchCode, string counterId, string clerkId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<CounterTransaction> AddRetailLineAsync(string transactionId, string actorStaffId, string sku, int quantity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<CounterTransaction> AddShippingLineAsync(string transactionId, string actorStaffId, Shipment shipment, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<CounterTransaction> AddMoneyOrderLineAsync(string transactionId, string actorStaffId, MoneyOrder order, CancellationToken cancellationToken = default)
        {
            var lineTotal = order.Amount + order.Fee;
            transaction.AddLine(new TransactionLine(Guid.NewGuid().ToString("N"), LineKind.MoneyOrder, "Money order", null, 1, lineTotal, 0m, lineTotal));
            return Task.FromResult(transaction);
        }
        public Task<CounterTransaction> CompleteAsync(string transactionId, string actorStaffId, PaymentMethod method, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<CounterTransaction> VoidAsync(string transactionId, string staffId, string reason, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<CounterTransaction> RefundAsync(string transactionId, string staffId, string reason, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
