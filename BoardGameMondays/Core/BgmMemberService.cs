using System;
namespace BoardGameMondays.Core
{
    public class BgmMemberService
    {
        public BgmMember? CurrentMember { get; private set; }

        public void SetMember(BgmMember member)
        {
            CurrentMember = member;
        }

        public void ClearMember()
        {
            CurrentMember = null;
        }
    }
}