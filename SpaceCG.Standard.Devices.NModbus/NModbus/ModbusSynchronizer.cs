using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NModbus;
using SpaceCG.NModbus.Data;

namespace SpaceCG.NModbus
{
    /// <summary>
    /// Modbus 总线 同步器
    /// </summary>
    public class ModbusSynchronizer
    {
        private readonly IModbusMaster _master;

        /// <summary>本地数据存储，读到的远端数据回写到这里。</summary>
        private readonly ISlaveDataStore _dataStore;

        public IModbusMaster ModbusMaster => _master;
        public ISlaveDataStore LocalDataStore => _dataStore;

        public ModbusSynchronizer(IModbusMaster master, ISlaveDataStore dataStore)
        {
            //ModbusSynchronization

            _master = master;
            _dataStore = dataStore;
        }

        public void AddRegisters(RegisterPoint registers)
        {

        }

        public void Start()
        {

        }

        public void Stop()
        {

        }


        public void Dispose()
        {

        }
    }
}
