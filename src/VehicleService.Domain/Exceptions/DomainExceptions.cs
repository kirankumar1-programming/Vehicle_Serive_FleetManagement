namespace VehicleService.Domain.Exceptions;

public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
    public DomainException(string message, Exception innerException) : base(message, innerException) { }
}

public class DoubleBookingException : DomainException
{
    public DoubleBookingException(string message) : base(message) { }
    public DoubleBookingException(string message, Exception innerException) : base(message, innerException) { }
}

public class InsufficientStockException : DomainException
{
    public InsufficientStockException(string message) : base(message) { }
    public InsufficientStockException(string message, Exception innerException) : base(message, innerException) { }
}

public class DuplicatePaymentException : DomainException
{
    public DuplicatePaymentException(string message) : base(message) { }
    public DuplicatePaymentException(string message, Exception innerException) : base(message, innerException) { }
}

public class InvalidStateTransitionException : DomainException
{
    public InvalidStateTransitionException(string message) : base(message) { }
    public InvalidStateTransitionException(string message, Exception innerException) : base(message, innerException) { }
}

public class WarrantyEligibilityException : DomainException
{
    public WarrantyEligibilityException(string message) : base(message) { }
    public WarrantyEligibilityException(string message, Exception innerException) : base(message, innerException) { }
}

public class MechanicUnavailableException : DomainException
{
    public MechanicUnavailableException(string message) : base(message) { }
    public MechanicUnavailableException(string message, Exception innerException) : base(message, innerException) { }
}

public class EntityNotFoundException : DomainException
{
    public EntityNotFoundException(string message) : base(message) { }
    public EntityNotFoundException(string message, Exception innerException) : base(message, innerException) { }
}

