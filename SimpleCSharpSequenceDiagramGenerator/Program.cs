using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text;

public class Program
{
    public static void Main(string[] args)
    {
        var sourceCode = File.ReadAllText(args[0]);
        var outputFile = args[1];

        var generator = new ComprehensiveSequenceDiagramGenerator();
        var diagram = generator.GenerateDiagram(sourceCode);
        Console.WriteLine(diagram);
        File.WriteAllText(outputFile, diagram);
        Console.WriteLine("Diagram has been written to " + outputFile + ". Application finished.");
    }
}

public class ComprehensiveSequenceDiagramGenerator
{
    private readonly StringBuilder _puml = new();
    private readonly StringBuilder _participantsStringBuilder = new();
    private readonly HashSet<string> _participants = new();
    private readonly Stack<string> _activeLoops = new();
    private readonly Stack<string> _asyncOperations = new();
    private int _indent = 0;
    private int _loopCounter = 0;
    private int _asyncCounter = 0;

    private void WriteIndentedLine(string line)
    {
        _puml.AppendLine(new string(' ', _indent * 2) + line);
    }

    private void WriteParticipantIndentedLine(string line)
    {
        _participantsStringBuilder.AppendLine(new string(' ', _indent * 2) + line);
    }

    public string GenerateDiagram(string sourceCode)
    {
        var tree = CSharpSyntaxTree.ParseText(sourceCode);
        var root = tree.GetRoot();

        WriteIndentedLine("@startuml");
        _indent++;

        // Find and analyze all method declarations
        var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
        foreach (var method in methodDeclarations)
        {
            AnalyzeMethod(method);
        }

        // Add all discovered participants at the beginning
        foreach (var participant in _participants)
        {
            WriteParticipantIndentedLine($"participant \"{participant}\"");
        }

        _indent--;
        WriteIndentedLine("@enduml");

        var finalString = _puml.ToString();
        finalString = finalString.Replace("@startuml",
            $"@startuml{Environment.NewLine}{_participantsStringBuilder.ToString()}{Environment.NewLine}");

        return finalString;
    }

    private void AnalyzeMethod(MethodDeclarationSyntax method)
    {
        var className = GetContainingClassName(method);
        AddParticipant(className);

        // Handle method parameters
        var parameters = method.ParameterList.Parameters
            .Select(p => $"{p.Type} {p.Identifier}")
            .ToList();

        WriteIndentedLine($"note over {className}: {method.Identifier}({string.Join(", ", parameters)})");

        if (method.Modifiers.Any(m => m.ValueText == "async"))
        {
            BeginAsyncOperation(className);
        }

        AnalyzeStatements(method.Body ?? (SyntaxNode)method.ExpressionBody, className);

        if (method.Modifiers.Any(m => m.ValueText == "async"))
        {
            EndAsyncOperation();
        }
    }

    private void AnalyzeStatements(SyntaxNode? node, string sourceClass)
    {
        if (node == null) return;

        foreach (var child in node.ChildNodes())
        {
            switch (child)
            {
                case InvocationExpressionSyntax invocation:
                    AnalyzeMethodInvocation(invocation, sourceClass);
                    break;

                case ObjectCreationExpressionSyntax creation:
                    AnalyzeObjectCreation(creation, sourceClass);
                    break;

                case IfStatementSyntax ifStatement:
                    AnalyzeControlFlow(ifStatement, sourceClass);
                    break;

                case ForStatementSyntax forStatement:
                    AnalyzeForLoop(forStatement, sourceClass);
                    break;

                case ForEachStatementSyntax foreachStatement:
                    AnalyzeForEachLoop(foreachStatement, sourceClass);
                    break;

                case WhileStatementSyntax whileStatement:
                    AnalyzeWhileLoop(whileStatement, sourceClass);
                    break;

                case TryStatementSyntax tryStatement:
                    AnalyzeTryCatch(tryStatement, sourceClass);
                    break;

                case SwitchStatementSyntax switchStatement:
                    AnalyzeSwitch(switchStatement, sourceClass);
                    break;

                default:
                    AnalyzeStatements(child, sourceClass);
                    break;
            }
        }
    }
    
        private void AnalyzeMethodInvocation(InvocationExpressionSyntax invocation, string sourceClass)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var targetObject = memberAccess.Expression.ToString();
            var methodName = memberAccess.Name.ToString();
            var arguments = invocation.ArgumentList.Arguments
                .Select(a => a.ToString())
                .ToList();

            for (int i = 0; i < arguments.Count; i++)
            {
                arguments[i] = arguments[i].Replace($"{System.Environment.NewLine}", "\\n");
            }

            AddParticipant(targetObject);

            var isAsync = methodName.EndsWith("Async");
            var callArrow = isAsync ? "->>" : "->";
            var returnArrow = isAsync ? "-->>" : "-->";

            WriteIndentedLine($"{sourceClass} {callArrow} \"{targetObject}\": {methodName}({string.Join(", ", arguments)})");

            // Handle return values
            if (invocation.Parent is AssignmentExpressionSyntax assignment)
            {
                var returnValue = assignment.Left.ToString();
                WriteIndentedLine($"{targetObject} {returnArrow} {sourceClass}: {returnValue}");
            }
            else if (invocation.Parent is ReturnStatementSyntax)
            {
                WriteIndentedLine($"{targetObject} {returnArrow} {sourceClass}: return value");
            }

            if (isAsync)
            {
                BeginAsyncOperation(targetObject);
            }
        }
    }

    private void AnalyzeObjectCreation(ObjectCreationExpressionSyntax creation, string sourceClass)
    {
        var targetType = creation.Type.ToString();
        AddParticipant(targetType);
        
        var arguments = creation.ArgumentList?.Arguments
            .Select(a => a.ToString())
            .ToList() ?? new List<string>();

        WriteIndentedLine($"{sourceClass} -> \"{targetType}\" **: new({string.Join(", ", arguments)})");
    }

    private void AnalyzeControlFlow(IfStatementSyntax ifStatement, string sourceClass)
    {
        var condition = ifStatement.Condition.ToString();
        WriteIndentedLine($"alt {condition}");
        _indent++;
        
        AnalyzeStatements(ifStatement.Statement, sourceClass);
        
        if (ifStatement.Else != null)
        {
            WriteIndentedLine("else");
            AnalyzeStatements(ifStatement.Else.Statement, sourceClass);
        }
        
        _indent--;
        WriteIndentedLine("end");
    }

    private void AnalyzeSwitch(SwitchStatementSyntax switchStatement, string sourceClass)
    {
        var expression = switchStatement.Expression.ToString();
        WriteIndentedLine($"alt [${expression}]");
        _indent++;

        foreach (var section in switchStatement.Sections)
        {
            var labels = section.Labels
                .OfType<CaseSwitchLabelSyntax>()
                .Select(l => l.Value.ToString())
                .ToList();

            if (labels.Any())
            {
                WriteIndentedLine($"case {string.Join(", ", labels)}");
                _indent++;
                AnalyzeStatements(section, sourceClass);
                _indent--;
            }
        }

        _indent--;
        WriteIndentedLine("end");
    }
    
        private void AnalyzeForLoop(ForStatementSyntax forStatement, string sourceClass)
    {
        var loopId = $"loop_{_loopCounter++}";
        var condition = forStatement.Condition?.ToString() ?? "true";
        
        _activeLoops.Push(loopId);
        WriteIndentedLine($"loop {condition}");
        _indent++;
        
        AnalyzeStatements(forStatement.Statement, sourceClass);
        
        _indent--;
        WriteIndentedLine("end");
        _activeLoops.Pop();
    }

    private void AnalyzeForEachLoop(ForEachStatementSyntax foreachStatement, string sourceClass)
    {
        var loopId = $"loop_{_loopCounter++}";
        var iteration = $"for each {foreachStatement.Identifier} in {foreachStatement.Expression}";
        
        _activeLoops.Push(loopId);
        WriteIndentedLine($"loop {iteration}");
        _indent++;
        
        AnalyzeStatements(foreachStatement.Statement, sourceClass);
        
        _indent--;
        WriteIndentedLine("end");
        _activeLoops.Pop();
    }

    private void AnalyzeWhileLoop(WhileStatementSyntax whileStatement, string sourceClass)
    {
        var loopId = $"loop_{_loopCounter++}";
        var condition = whileStatement.Condition.ToString();
        
        _activeLoops.Push(loopId);
        WriteIndentedLine($"loop while {condition}");
        _indent++;
        
        AnalyzeStatements(whileStatement.Statement, sourceClass);
        
        _indent--;
        WriteIndentedLine("end");
        _activeLoops.Pop();
    }

    private void AnalyzeTryCatch(TryStatementSyntax tryStatement, string sourceClass)
    {
        WriteIndentedLine("group try");
        _indent++;
        AnalyzeStatements(tryStatement.Block, sourceClass);
        _indent--;

        foreach (var catchClause in tryStatement.Catches)
        {
            var exceptionType = catchClause.Declaration?.Type.ToString() ?? "Exception";
            var exceptionVar = catchClause.Declaration?.Identifier.ToString() ?? "ex";
            
            WriteIndentedLine($"group catch {exceptionType} as {exceptionVar}");
            _indent++;
            AnalyzeStatements(catchClause.Block, sourceClass);
            _indent--;
            WriteIndentedLine("end");
        }

        if (tryStatement.Finally != null)
        {
            WriteIndentedLine("group finally");
            _indent++;
            AnalyzeStatements(tryStatement.Finally.Block, sourceClass);
            _indent--;
            WriteIndentedLine("end");
        }
        else
        {
            WriteIndentedLine("end");
        }
    }

    private void BeginAsyncOperation(string participant)
    {
        var asyncId = $"async_{_asyncCounter++}";
        _asyncOperations.Push(asyncId);
        WriteIndentedLine($"activate {participant}");
    }

    private void EndAsyncOperation()
    {
        if (_asyncOperations.Count > 0)
        {
            var asyncId = _asyncOperations.Pop();
            WriteIndentedLine($"deactivate {asyncId}");
        }
    }

    private string GetContainingClassName(MethodDeclarationSyntax method)
    {
        var classDeclaration = method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        return classDeclaration?.Identifier.Text ?? "UnknownClass";
    }

    private void AddParticipant(string participant)
    {
        if (!string.IsNullOrWhiteSpace(participant))
        {
            _participants.Add(participant);
        }
    }
}