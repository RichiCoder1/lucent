var result = Fixture.Application.Create();
Console.WriteLine(result);
return result == "{\"Name\":\"Ada\"}" ? 0 : 1;
