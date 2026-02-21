
namespace AprNes
{
    unsafe public class Mapper004RevA : Mapper004
    {
        public override void Mapper04step_IRQ()
        {
            int oldCounter = IRQCounter;
            bool wasReset = IRQReset;
            bool reload = (IRQCounter == 0 || IRQReset);
            if (reload)
                IRQCounter = IRQlatchVal;
            else
                IRQCounter--;
            IRQReset = false;

            // Rev A: fire when counter reaches 0 AND either:
            //   - old counter was non-zero (natural decrement to 0), OR
            //   - explicit reload was requested via $C001 write (IRQReset)
            // Does NOT fire when counter auto-reloads to 0 from already being 0
            if (IRQCounter == 0 && IRQ_enable && (oldCounter != 0 || wasReset))
                NesCore.statusmapperint = true;
        }
    }
}
