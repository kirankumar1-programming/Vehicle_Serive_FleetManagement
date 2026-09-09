namespace VehicleService.Domain.Exceptions;

public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
    public DomainException(string message, Exception innerException) : base(message, innerException) { }
}

public class DoubleBookingException : DomainException
{
    public DoubleBookingException(string message) : base(message) { }
}

public class InsufficientStockException : DomainException
{
    public InsufficientStockException(string message) : base(message) { }
}

public class DuplicatePaymentException : DomainException
{
    public DuplicatePaymentException(string message) : base(message) { }
}

public class InvalidStateTransitionException : DomainException
{
    public InvalidStateTransitionException(string message) : base(message) { }
}

public class WarrantyEligibilityException : DomainException
{
    public WarrantyEligibilityException(string message) : base(message) { }
}

public class MechanicUnavailableException : DomainException
{
    public MechanicUnavailableException(string message) : base(message) { }
}
