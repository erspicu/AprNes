<html><head><meta name="color-scheme" content="light dark"></head><body><pre style="word-wrap: break-word; white-space: pre-wrap;">#pragma once 
#include "pch.h"

#include "Shared/SettingTypes.h"
#include "Shared/Interfaces/IConsole.h"
#include "Debugger/DebugTypes.h"
#include "Utilities/safe_ptr.h"

class Emulator;
class NesCpu;
class BaseNesPpu;
class NesApu;
class NesMemoryManager;
class NesControlManager;
class BaseMapper;
class EmuSettings;
class NesSoundMixer;
class BaseVideoFilter;
class BaseControlManager;
class HdAudioDevice;
class HdPackBuilder;
class Epsm;
struct HdPackData;
struct HdPackBuilderOptions;

enum class DebugEventType;
enum class EventType;
enum class ConsoleRegion;
enum class GameInputType;
enum class GameSystem;

class NesConsole final : public IConsole
{
private:
	Emulator* _emu = nullptr;

	unique_ptr&lt;NesConsole&gt; _vsSubConsole;
	NesConsole* _vsMainConsole = nullptr;

	unique_ptr&lt;NesCpu&gt; _cpu;
	unique_ptr&lt;BaseNesPpu&gt; _ppu;
	unique_ptr&lt;NesApu&gt; _apu;
	unique_ptr&lt;NesMemoryManager&gt; _memoryManager;
	unique_ptr&lt;BaseMapper&gt; _mapper;
	unique_ptr&lt;NesControlManager&gt; _controlManager;
	unique_ptr&lt;NesSoundMixer&gt; _mixer;

	safe_ptr&lt;HdPackData&gt; _hdData;
	unique_ptr&lt;HdAudioDevice&gt; _hdAudioDevice;
	unique_ptr&lt;HdPackBuilder&gt; _hdPackBuilder;

	ConsoleRegion _region = ConsoleRegion::Auto;

	bool _nextFrameOverclockDisabled = false;
	
	void UpdateRegion(bool forceUpdate = false);
	void LoadHdPack(VirtualFile&amp; romFile);
	
	void InitializeInputDevices(GameInputType inputType, GameSystem system);

	void StartRecordingHdPack(HdPackBuilderOptions options);
	void StopRecordingHdPack();

public:
	NesConsole(Emulator* emulator);
	~NesConsole();

	static vector&lt;string&gt; GetSupportedExtensions() { return { ".nes", ".fds", ".unif", ".unf", ".nsf", ".nsfe", ".studybox", ".qd" }; }
	static vector&lt;string&gt; GetSupportedSignatures() { return { "NES\x1a", "FDS\x1a", "\x1*NINTENDO-HVC*", "NESM\x1a", "NSFE", "UNIF", "STBX" }; }

	NesCpu* GetCpu() { return _cpu.get(); }
	BaseNesPpu* GetPpu() { return _ppu.get(); }
	NesApu* GetApu() { return _apu.get(); }
	NesMemoryManager* GetMemoryManager() { return _memoryManager.get(); }
	BaseMapper* GetMapper() { return _mapper.get(); }
	NesSoundMixer* GetSoundMixer() { return _mixer.get(); }
	Emulator* GetEmulator();
	NesConfig&amp; GetNesConfig();

	void ProcessCpuClock();

	Epsm* GetEpsm();

	NesConsole* GetVsMainConsole();
	NesConsole* GetVsSubConsole();
	bool IsVsMainConsole();
	void RunVsSubConsole();

	void SetNextFrameOverclockStatus(bool disabled);

	// Inherited via IConsole
	void Serialize(Serializer&amp; s) override;
	void Reset() override;
	LoadRomResult LoadRom(VirtualFile&amp; romFile) override;
	void RunFrame() override;
	BaseControlManager* GetControlManager() override;
	double GetFps() override;
	PpuFrameInfo GetPpuFrame() override;
	ConsoleRegion GetRegion() override;
	ConsoleType GetConsoleType() override;
	vector&lt;CpuType&gt; GetCpuTypes() override;

	AddressInfo GetAbsoluteAddress(AddressInfo&amp; relAddress) override;
	AddressInfo GetRelativeAddress(AddressInfo&amp; absAddress, CpuType cpuType) override;
	void GetConsoleState(BaseState&amp; state, ConsoleType consoleType) override;

	uint64_t GetMasterClock() override;
	uint32_t GetMasterClockRate() override;

	void SaveBattery() override;
	
	ShortcutState IsShortcutAllowed(EmulatorShortcut shortcut, uint32_t shortcutParam) override;

	BaseVideoFilter* GetVideoFilter(bool getDefaultFilter) override;

	string GetHash(HashType hashType) override;
	RomFormat GetRomFormat() override;
	AudioTrackInfo GetAudioTrackInfo() override;
	void ProcessAudioPlayerAction(AudioPlayerActionParams p) override;
	uint8_t DebugRead(uint16_t addr);
	void DebugWrite(uint16_t addr, uint8_t value, bool disableSideEffects);
	uint8_t DebugReadVram(uint16_t addr);
	void DebugWriteVram(uint16_t addr, uint8_t value);

	void ProcessCheatCode(InternalCheatCode&amp; code, uint32_t addr, uint8_t&amp; value) override;
	void InitializeRam(void* data, uint32_t length);
	DipSwitchInfo GetDipSwitchInfo() override;

	void ProcessNotification(ConsoleNotificationType type, void* parameter) override;
};
</pre></body></html>