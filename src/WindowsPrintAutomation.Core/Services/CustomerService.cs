using WindowsPrintAutomation.Core.Data;
using WindowsPrintAutomation.Core.Domain.Entities;

namespace WindowsPrintAutomation.Core.Services;

public interface ICustomerService
{
    Task<List<Customer>> SearchCustomersAsync(string query);
    Task<Customer?> GetCustomerByIdAsync(long id);
    Task<Customer?> GetCustomerByCodeAsync(string code);
    Task<Customer> CreateCustomerAsync(string fullName, string phoneNumber, string? notes = null, string? customCode = null);
    Task<bool> UpdateCustomerAsync(Customer customer);
    Task<string> GenerateNextCustomerCodeAsync();
}

public class CustomerService : ICustomerService
{
    private readonly DatabaseService _databaseService;

    public CustomerService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task<List<Customer>> SearchCustomersAsync(string query)
    {
        var list = new List<Customer>();
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();

        if (string.IsNullOrWhiteSpace(query))
        {
            cmd.CommandText = "SELECT Id, CustomerCode, FullName, PhoneNumber, Notes, CreatedAt FROM Customers ORDER BY CreatedAt DESC LIMIT 50";
        }
        else
        {
            cmd.CommandText = @"
                SELECT Id, CustomerCode, FullName, PhoneNumber, Notes, CreatedAt 
                FROM Customers 
                WHERE CustomerCode LIKE @q OR FullName LIKE @q OR PhoneNumber LIKE @q
                ORDER BY CreatedAt DESC LIMIT 50";
            cmd.Parameters.AddWithValue("@q", $"%{query}%");
        }

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new Customer
            {
                Id = reader.GetInt64(0),
                CustomerCode = reader.GetString(1),
                FullName = reader.GetString(2),
                PhoneNumber = reader.GetString(3),
                Notes = reader.IsDBNull(4) ? null : reader.GetString(4),
                CreatedAt = DateTime.Parse(reader.GetString(5))
            });
        }
        return list;
    }

    public async Task<Customer?> GetCustomerByIdAsync(long id)
    {
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, CustomerCode, FullName, PhoneNumber, Notes, CreatedAt FROM Customers WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new Customer
            {
                Id = reader.GetInt64(0),
                CustomerCode = reader.GetString(1),
                FullName = reader.GetString(2),
                PhoneNumber = reader.GetString(3),
                Notes = reader.IsDBNull(4) ? null : reader.GetString(4),
                CreatedAt = DateTime.Parse(reader.GetString(5))
            };
        }
        return null;
    }

    public async Task<Customer?> GetCustomerByCodeAsync(string code)
    {
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, CustomerCode, FullName, PhoneNumber, Notes, CreatedAt FROM Customers WHERE CustomerCode = @code";
        cmd.Parameters.AddWithValue("@code", code);
        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new Customer
            {
                Id = reader.GetInt64(0),
                CustomerCode = reader.GetString(1),
                FullName = reader.GetString(2),
                PhoneNumber = reader.GetString(3),
                Notes = reader.IsDBNull(4) ? null : reader.GetString(4),
                CreatedAt = DateTime.Parse(reader.GetString(5))
            };
        }
        return null;
    }

    public async Task<string> GenerateNextCustomerCodeAsync()
    {
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Customers";
        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        return $"CUST-{(count + 1):D4}";
    }

    public async Task<Customer> CreateCustomerAsync(string fullName, string phoneNumber, string? notes = null, string? customCode = null)
    {
        var code = customCode;
        if (string.IsNullOrWhiteSpace(code))
        {
            code = await GenerateNextCustomerCodeAsync();
        }

        var customer = new Customer
        {
            CustomerCode = code,
            FullName = fullName.Trim(),
            PhoneNumber = phoneNumber.Trim(),
            Notes = notes?.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Customers (CustomerCode, FullName, PhoneNumber, Notes, CreatedAt)
            VALUES (@code, @name, @phone, @notes, @created)
            RETURNING Id;";
        cmd.Parameters.AddWithValue("@code", customer.CustomerCode);
        cmd.Parameters.AddWithValue("@name", customer.FullName);
        cmd.Parameters.AddWithValue("@phone", customer.PhoneNumber);
        cmd.Parameters.AddWithValue("@notes", (object?)customer.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created", customer.CreatedAt.ToString("o"));

        var id = await cmd.ExecuteScalarAsync();
        customer.Id = Convert.ToInt64(id);
        return customer;
    }

    public async Task<bool> UpdateCustomerAsync(Customer customer)
    {
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Customers SET
                FullName = @name,
                PhoneNumber = @phone,
                Notes = @notes
            WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", customer.Id);
        cmd.Parameters.AddWithValue("@name", customer.FullName);
        cmd.Parameters.AddWithValue("@phone", customer.PhoneNumber);
        cmd.Parameters.AddWithValue("@notes", (object?)customer.Notes ?? DBNull.Value);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }
}
