# Contributing to Intune Commander

Thank you for your interest in contributing to Intune Commander! This document provides guidelines for submitting pull requests and maintaining code quality.

## Pull Request Guidelines

### PR Title Convention

Use conventional commit format with priority labels:

```
<type>(<scope>): <description> [Priority]

Examples:
feat(auth): add certificate authentication [P1]
fix(ui): resolve dark mode contrast issue [P1]
docs: update ARCHITECTURE.md with cache patterns [P2]
refactor(services): extract common pagination helper [P3]
test(core): add unit tests for ProfileService [P2]
```

**Types:**
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation only
- `refactor`: Code restructuring without behavior change
- `test`: Adding or updating tests
- `style`: Code style changes (formatting, etc.)
- `perf`: Performance improvements
- `chore`: Maintenance tasks, dependencies

**Priorities:**
- `[P1]` - **Critical**: Security issues, broken features, blocking bugs
- `[P2]` - **Important**: Significant improvements, major bugs, essential docs
- `[P3]` - **Enhancement**: Nice-to-have features, optimizations, minor fixes

### PR Description Template

Every PR should include these sections:

```markdown
## Summary
Brief description of what this PR does and why.

## Changes
- Bullet list of specific modifications
- Each change on its own line
- Link to related issues if applicable

## Test Plan
- [ ] `dotnet build` - no errors/warnings
- [ ] `dotnet test` - all tests pass
- [ ] Manual testing: describe steps taken
- [ ] Specific scenarios verified

## Breaking Changes
If any breaking changes exist:
- Describe what breaks
- Provide migration path
- Update CHANGELOG.md

## Documentation
- [ ] Updated relevant documentation
- [ ] Added code comments where needed
- [ ] Updated CHANGELOG.md if user-facing
```

### Before Submitting

1. **Build and Test Locally**
   ```bash
   dotnet build
   dotnet test
   ```

2. **Follow Code Conventions**
   - Review `.github/copilot-instructions.md`
   - Review `CLAUDE.md` for architectural patterns
   - Match existing code style

3. **Update Documentation**
   - Update CHANGELOG.md for user-facing changes
   - Update ARCHITECTURE.md if changing patterns
   - Add XML doc comments for public APIs

4. **Commit Messages**
   - Use meaningful commit messages
   - Reference issue numbers where applicable
   - Keep commits focused and atomic

## Code Standards

### C# Conventions

- **Namespace**: `IntuneManager.Core.*` or `IntuneManager.Desktop.*`
- **Nullable reference types**: Enabled everywhere
- **Private fields**: `_camelCase`
- **Public members**: `PascalCase`
- **ViewModels**: Must be `partial class` for CommunityToolkit.Mvvm
- **Async methods**: Always end with `Async`, always accept `CancellationToken`

### Graph API Patterns

**Manual Pagination (Required)**
```csharp
var response = await _graphClient.SomeEndpoint
    .GetAsync(req => req.QueryParameters.Top = 999, cancellationToken);

var result = new List<SomeType>();
while (response != null)
{
    if (response.Value != null)
        result.AddRange(response.Value);

    if (!string.IsNullOrEmpty(response.OdataNextLink))
    {
        response = await _graphClient.SomeEndpoint
            .WithUrl(response.OdataNextLink)
            .GetAsync(cancellationToken: cancellationToken);
    }
    else
    {
        break;
    }
}
```

**Never use `PageIterator`** - it silently truncates results on some tenants.

### UI Thread Rules

**Critical**: Never block the UI thread with `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()`

```csharp
// ❌ BAD - blocks UI thread
var result = SomeAsyncMethod().Result;

// ✅ GOOD - async all the way
var result = await SomeAsyncMethod();

// ✅ GOOD - fire and forget for non-blocking loads
_ = LoadDataAsync();
```

### Service Implementation Pattern

Every Intune object type service follows this pattern:

```csharp
public interface ISomeService
{
    Task<List<SomeType>> ListAsync(CancellationToken cancellationToken = default);
    Task<SomeType?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<SomeType> CreateAsync(SomeType item, CancellationToken cancellationToken = default);
    Task<SomeType> UpdateAsync(string id, SomeType item, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<List<Assignment>> GetAssignmentsAsync(string id, CancellationToken cancellationToken = default);
}

public class SomeService : ISomeService
{
    private readonly GraphServiceClient _graphClient;

    public SomeService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    // Implementation with manual pagination on List methods
}
```

## Testing Requirements

### Unit Tests

- Use xUnit with `[Fact]` or `[Theory]`
- Test files mirror source structure
- Test class name: `{ClassUnderTest}Tests`
- Cover: happy path, edge cases, null handling, exceptions

### Test Conventions

```csharp
[Fact]
public async Task MethodName_Scenario_ExpectedBehavior()
{
    // Arrange
    var service = new SomeService(mockClient);

    // Act
    var result = await service.SomeMethodAsync();

    // Assert
    Assert.NotNull(result);
}
```

## Documentation Standards

### Code Comments

- Add XML doc comments for public APIs
- Use `//` comments sparingly, only for complex logic
- Code should be self-documenting where possible

### Documentation Files

- **CHANGELOG.md**: User-facing changes, release notes
- **ARCHITECTURE.md**: Architectural decisions and patterns
- **README.md**: Getting started, build instructions
- **PR_STATUS.md**: Current PR organization (update as needed)

## Review Process

### What Reviewers Look For

1. **Correctness**: Does it work as intended?
2. **Tests**: Are there sufficient tests?
3. **Code Quality**: Is it maintainable and readable?
4. **Documentation**: Are changes documented?
5. **Breaking Changes**: Are they necessary and documented?
6. **Performance**: Any obvious performance issues?
7. **Security**: No credentials, sensitive data, or vulnerabilities?

### Addressing Review Feedback

- Respond to all comments
- Make requested changes or explain why not
- Push new commits (don't force-push during review)
- Re-request review after addressing feedback

## PR Priorities and Merge Order

See [PR_STATUS.md](PR_STATUS.md) for current PR organization and recommended merge order.

**General Priority Order:**
1. **P1 (Critical)**: Security fixes, broken features, blocking bugs
2. **P2 (Important)**: Documentation accuracy, significant improvements
3. **P3 (Enhancement)**: Nice-to-have features, optimizations

**Dependencies:**
- Check PR_STATUS.md for PR dependencies
- Base your branch on `main` unless depending on another PR
- Coordinate with maintainers if multiple PRs affect same code

## Getting Help

- **Questions about implementation**: Check `CLAUDE.md` and `ARCHITECTURE.md`
- **Questions about PR process**: See `PR_STATUS.md`
- **Questions about code patterns**: See `.github/copilot-instructions.md`
- **Stuck on something?**: Open a draft PR and ask for guidance

## Wave Implementation

The project uses a Wave system for implementing new Intune object types. See `docs/issues/` for detailed tracking and current status:

- **Wave 1**: Endpoint Security, Admin Templates, Enrollment
- **Wave 2**: App Protection, Managed App Configs
- **Wave 3**: Tenant Administration
- **Wave 4**: Autopilot, Device Management
- **Wave 5**: Conditional Access, Identity

If contributing a new service, refer to the appropriate Wave document for detailed requirements and up-to-date progress.

## License

By contributing to Intune Commander, you agree that your contributions will be licensed under the [MIT License](LICENSE).
