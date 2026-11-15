using Xunit;

// Classes in this assembly each start an in-memory API host with its own database. Running them one at a time
// keeps LocalDB load predictable; tests inside a class are already isolated by unique tracking numbers.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
