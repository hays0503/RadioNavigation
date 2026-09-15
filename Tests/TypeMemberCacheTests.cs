using System.Reflection;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class TypeMemberCacheTests
    {
        private sealed class Sample
        {
            public int Prop { get; set; }
            public int Field;
            public static int StaticProp { get; set; }
            public static int StaticField;
        }

        private sealed class Other
        {
            public string Prop { get; set; }
        }

        [Fact]
        public void GetInstanceMember_ResolvesProperty()
        {
            MemberInfo member = TypeMemberCache.GetInstanceMember(typeof(Sample), "Prop");
            Assert.IsAssignableFrom<PropertyInfo>(member);
            Assert.Equal("Prop", member.Name);
        }

        [Fact]
        public void GetInstanceMember_ResolvesField_WhenNoProperty()
        {
            MemberInfo member = TypeMemberCache.GetInstanceMember(typeof(Sample), "Field");
            Assert.IsAssignableFrom<FieldInfo>(member);
            Assert.Equal("Field", member.Name);
        }

        [Fact]
        public void GetInstanceMember_IsCached_SameInstance()
        {
            MemberInfo first = TypeMemberCache.GetInstanceMember(typeof(Sample), "Prop");
            MemberInfo second = TypeMemberCache.GetInstanceMember(typeof(Sample), "Prop");
            Assert.Same(first, second);
        }

        [Fact]
        public void GetInstanceMember_DifferentTypes_DoNotCollide()
        {
            MemberInfo a = TypeMemberCache.GetInstanceMember(typeof(Sample), "Prop");
            MemberInfo b = TypeMemberCache.GetInstanceMember(typeof(Other), "Prop");
            Assert.NotSame(a, b);
            Assert.Equal(typeof(Other), b.DeclaringType);
        }

        [Fact]
        public void GetInstanceMember_Missing_ReturnsNull()
        {
            Assert.Null(TypeMemberCache.GetInstanceMember(typeof(Sample), "DefinitelyMissing"));
        }

        [Fact]
        public void GetStaticMember_ResolvesPropertyAndField()
        {
            Assert.IsAssignableFrom<PropertyInfo>(TypeMemberCache.GetStaticMember(typeof(Sample), "StaticProp"));
            Assert.IsAssignableFrom<FieldInfo>(TypeMemberCache.GetStaticMember(typeof(Sample), "StaticField"));
        }

        [Fact]
        public void GetStaticMember_InstanceMember_ReturnsNull()
        {
            Assert.Null(TypeMemberCache.GetStaticMember(typeof(Sample), "Prop"));
        }

        [Fact]
        public void Clear_DropsEntries_ResolveStillWorks()
        {
            TypeMemberCache.GetInstanceMember(typeof(Sample), "Prop");
            TypeMemberCache.Clear();
            Assert.NotNull(TypeMemberCache.GetInstanceMember(typeof(Sample), "Prop"));
        }
    }
}
