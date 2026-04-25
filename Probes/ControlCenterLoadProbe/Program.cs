using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

const string defaultInstallDir = @"C:\Program Files (x86)\NL5\ControlCenter";

var installDir = args.Length > 0 ? args[0] : defaultInstallDir;
var assemblyPath = Path.Combine(installDir, "ControlCenter.exe");

Console.WriteLine($"framework={RuntimeInformation.FrameworkDescription}");
Console.WriteLine($"assemblyPath={assemblyPath}");

var assembly = Assembly.LoadFrom(assemblyPath);
Console.WriteLine($"loaded={assembly.FullName}");

var myWmiBaseType = RequireType(assembly, "ControlCenterSpace.MyWmiBase");
var init = RequireMethod(myWmiBaseType, "Init", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, Type.EmptyTypes);
var wmiInstance = Activator.CreateInstance(myWmiBaseType);
Console.WriteLine($"wmiInstanceCreated={wmiInstance != null}");
Console.WriteLine($"wmiInit={Convert.ToBoolean(init.Invoke(null, null), CultureInfo.InvariantCulture)}");

var powerPlanType = RequireType(assembly, "ControlCenterSpace.BIOS.MyPowerPlan");
var powerPlan = Activator.CreateInstance(powerPlanType);
Console.WriteLine($"powerPlanCreated={powerPlan != null}");

var initSchemeGuid = RequireMethod(powerPlanType, "init_scheme_guid", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes);
initSchemeGuid.Invoke(powerPlan, null);

var scheme = RequireProperty(powerPlanType, "MyScheme").GetValue(powerPlan);
Console.WriteLine($"scheme={Convert.ToString(scheme, CultureInfo.InvariantCulture)}");

static Type RequireType(Assembly assembly, string fullName)
{
    return assembly.GetType(fullName, throwOnError: true)!;
}

static MethodInfo RequireMethod(Type type, string name, BindingFlags flags, params Type[] parameterTypes)
{
    return type.GetMethod(name, flags, null, parameterTypes, null)
        ?? throw new MissingMethodException(type.FullName, name);
}

static PropertyInfo RequireProperty(Type type, string name)
{
    return type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
        ?? throw new MissingMemberException(type.FullName, name);
}
