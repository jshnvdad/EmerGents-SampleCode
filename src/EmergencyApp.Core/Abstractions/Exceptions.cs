namespace EmergencyApp.Core.Abstractions;

public abstract class DomainException(string message) : Exception(message);
public sealed class NotFoundException(string message) : DomainException(message);
public sealed class ForbiddenException(string message) : DomainException(message);
public sealed class ConflictException(string message) : DomainException(message);
public sealed class InvalidInputException(string message) : DomainException(message);
