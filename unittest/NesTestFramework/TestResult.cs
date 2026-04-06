namespace NesTestFramework
{
    public class TestResult
    {
        public TestDefinition Test;
        public bool Passed;
        public int ResultCode;        // 0=pass, 1-254=fail code, 255=unknown/timeout
        public string ResultText;     // blargg text or detection description
        public int FrameCount;        // frames elapsed
        public string DetectionMethod; // "$6000 protocol" / "screen stability" / "nametable text" / "timeout"
    }
}
