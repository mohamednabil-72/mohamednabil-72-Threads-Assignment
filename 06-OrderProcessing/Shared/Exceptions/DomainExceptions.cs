namespace OrderProcessing.Shared.Exceptions;

public sealed class CustomerNotFoundException(string message) : Exception(message) { }
public sealed class InvalidCouponException(string message) : Exception(message) { }
public sealed class ShippingProviderUnavailableException(string message) : Exception(message) { }
public sealed class DatabaseWriteException(string message) : Exception(message) { }
public sealed class SmtpUnavailableException(string message) : Exception(message) { }
