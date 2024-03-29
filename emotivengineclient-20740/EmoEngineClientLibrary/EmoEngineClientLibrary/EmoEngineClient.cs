﻿// Copyright © 2009 James Galasyn 

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;

using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace EmoEngineClientLibrary
{
    /// <summary>
    /// Provides event-driven access to the <see cref="EmoEngine"/> class.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use the <see cref="EmoEngineClient"/> class to interact with the <see cref="EmoEngine"/> class
    /// provided by Emotiv in the managed assembly, DotNetEmotivSDK.dll.
    /// </para>
    /// <para>
    /// The <see cref="EmoEngineClient"/> class interacts with the <see cref="EmoEngine"/> class in two distinct ways.
    /// <list type="bullet">
    /// <item>Polling for neuroheadset device state, which is reported in <see cref="EmoState"/> instances. 
    /// A worker thread polls the <see cref="EmoEngine.ProcessEvents(int)"/> method. </item>
    /// <item>Polling for realtime electrode data from the neuroheadset device, which is reported in dictionaries. 
    /// A worker thread polls the <see cref="EmoEngine.GetData"/> method. </item>
    /// </list>
    /// 
    /// </para>
    /// </remarks>
    public class EmoEngineClient : INotifyPropertyChanged, IDisposable
    {
        ///////////////////////////////////////////////////////////////////////
        #region Public Properties

        /// <summary>
        /// Gets the most recent EmotivState from the EmotivEngine.
        /// </summary>
        public EmotivState CurrentEmotivState
        {
            get
            {
                return this._emotivState;
            }
        }

        /// <summary>
        /// Gets the circular buffer that holds realtime electrode data.
        /// </summary>
        public SampleBuffer Buffer
        {
            get
            {
                if( this._sampleBuffer == null )
                {
                    this._sampleBuffer = new SampleBuffer( this.BufferSize );
                    //this._sampleBuffer.BufferFilled += new BufferFilledEventHandler( sampleBuffer_BufferFilled );
                }

                return this._sampleBuffer;
            }
        }

        /// <summary>
        /// Gets the size of the circular buffer, in samples.
        /// </summary>
        /// <remarks>Set the <see cref="BufferSizeFactor"/> property to change the value
        /// of <see cref="BufferSize"/>. </remarks>
        public int BufferSize
        {
            get
            {
                return ( (int)(this.BufferSizeFactor * this.SamplingRate) );
            }
        }

        /// <summary>
        /// Gets or sets the factor (in seconds) by which to scale the 
        /// <see cref="BufferSize"/> property.
        /// </summary>
        public double BufferSizeFactor
        {
            get
            {
                return this._bufferSizeFactor;
            }

            set
            {
                if( value > 0 )
                {
                    if( this._bufferSizeFactor != value )
                    {
                        this._bufferSizeFactor = value;

                        //BufferFilledEventHandler handler = this._sampleBuffer.BufferFilled;
                        //this._sampleBuffer = new SampleBuffer( this.BufferSize );

                        this.NotifyPropertyChanged( "BufferSizeFactor" );
                        this.NotifyPropertyChanged( "BufferSize" );
                    }
                }
            }
        }

        /// <summary>
        /// Gets the port for connecting to the EmoComposer application.
        /// </summary>
        public static ushort EmoComposerPort
        {
            get
            {
                return _composerPort;
            }
        }

        /// <summary>
        /// Gets the port for connecting to the Emotiv Control Panel application.
        /// </summary>
        public static ushort ControlPanelPort
        {
            get
            {
                return _controlPanelPort;
            }
        }

        /// <summary>
        /// Gets the IP address of the EmotivEngine.
        /// </summary>
        public string EmoEngineTargetIP
        {
            get
            {
                return _targetIP;
            }
        }

        /// <summary>
        /// Gets or sets the ID for the current user.
        /// </summary>
        public uint UserID
        {
            get
            {
                return this._userId;
            }

            set
            {
                if( value != this._userId )
                {
                    this._userId = value;
                    this.NotifyPropertyChanged( "UserID" );
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether electrode data from the EmoEngine can be polled.
        /// </summary>
        /// <remarks>
        /// The <see cref="CanStartPolling"/> property is useful for displaying device state 
        /// in the user interface.
        /// </remarks>
        public bool CanStartPolling
        {
            get
            {
                return ( this.IsEmoEngineRunning && !this.IsPolling );
            }
        }

        /// <summary>
        /// Gets a value indicating whether a stop request for electrode data polling is valid.
        /// </summary>
        /// <remarks>
        /// The <see cref="CanStopPolling"/> property is useful for displaying device state 
        /// in the user interface.
        /// </remarks>
        public bool CanStopPolling
        {
            get
            {
                return ( this.IsPolling );
            }
        }

        /// <summary>
        /// Gets a value indicating whether the worker threads are active.
        /// </summary>
        /// <summary>
        /// Gets a value indicating whether a stop request for electrode data polling is valid.
        /// </summary>
        /// <remarks>
        /// The <see cref="IsPolling"/> property is useful for displaying device state 
        /// in the user interface.
        /// </remarks>
        public bool IsPolling
        {
            get
            {
                return this._isPolling;
            }

            private set
            {
                if( value != this._isPolling )
                {
                    this._isPolling = value;
                    this.NotifyPropertyChanged( "IsPolling" );
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether the <see cref="EmoEngine"/> can be polled.
        /// </summary>
        /// <remarks>
        /// The <see cref="CanStartEmoEngine"/> property is useful for displaying device state 
        /// in the user interface.
        /// </remarks>
        public bool CanStartEmoEngine
        {
            get
            {
                return ( this._emotivEngine == null && !this.IsEmoEngineRunning );
            }
        }

        /// <summary>
        /// Gets a value indicating whether the <see cref="EmoEngine"/> is currently polling.
        /// </summary>
        /// <remarks>
        /// The <see cref="CanStopEmoEngine"/> property is useful for displaying device state 
        /// in the user interface.
        /// </remarks>
        public bool CanStopEmoEngine
        {
            get
            {
                return ( this.IsEmoEngineRunning );
            }
        }

        /// <summary>
        /// Gets a value indicating whether the <see cref="EmoEngine"/> is initialized and active.
        /// </summary>
        /// <remarks>
        /// The <see cref="IsEmoEngineRunning"/> property is useful for displaying device state 
        /// in the user interface.
        /// </remarks>
        public bool IsEmoEngineRunning
        {
            get
            {
                return this._isEmoEngineRunning;
            }

            private set
            {
                if( value != this._isEmoEngineRunning )
                {
                    this._isEmoEngineRunning = value;
                    this.NotifyPropertyChanged( "IsEmoEngineRunning" );
                }
            }
        }

        /// <summary>
        /// Gets or sets the port for connecting to the EmotivEngine.
        /// </summary>
        public ushort ActivePort
        {
            get
            {
                return this._activePort;
            }

            set
            {
                if( value != ControlPanelPort &&
                    value != EmoComposerPort )
                {
                    throw new ArgumentException( "must be either ControlPanelPort or EmoComposerPort", "ActivePort" );
                }

                if( value != this._activePort )
                {
                    this._activePort = value;
                    this.NotifyPropertyChanged( "ActivePort" );
                }
            }
        }

        /// <summary>
        /// Gets or sets the period, in milliseconds, at which the EmoEngine is polled. 
        /// </summary>
        public int EmoEnginePollingPeriod
        {
            get
            {
                return this._emoEnginePollingPeriod;
            }

            set
            {
                if( value <= 0 ||
                    value > _emotivEngineTimeout )
                {
                    throw new ArgumentException( "must be > 0 and <= _emotivEngineTimeout", "EmoEnginePollingPeriod" );
                }

                if( value != this._emoEnginePollingPeriod )
                {
                    this._emoEnginePollingPeriod = value;
                    this.NotifyPropertyChanged( "EmoEnginePollingPeriod" );
                }
            }
        }

        /// <summary>
        /// Gets the period, in Hertz, at which data frames are polled. 
        /// </summary>
        /// <remarks>
        /// <para>
        /// Setting the <see cref="SamplingRate"/> property also sets the
        /// <see cref="DataPollingPeriod"/> property, which is computed by using
        /// the following formula.
        /// </para>
        /// <para>
        /// DataPollingPeriod = 1 / SamplingRate * 1000
        /// </para>
        /// The <see cref="SamplingRate"/> property is specified by the headset
        /// and usually is set by calling the <see cref="EmoEngine.DataGetSamplingRate"/> method.
        /// </remarks>
        public int SamplingRate
        {
            get
            {
                return this._samplingRate;
            }

            private set
            {
                if( value <= 0 ||
                    value > _maxSamplingRate )
                {
                    throw new ArgumentException( "must be > 0 and <= _maxSamplingRate", "SamplingRate" );
                }

                if( value != this._samplingRate )
                {
                    this._samplingRate = value;
                    this.DataPollingPeriod = (int)( ( 1.0d / (double)this._samplingRate ) * 1000 );
                    this.NotifyPropertyChanged( "SamplingRate" );
                }
            }
        }

        /// <summary>
        /// Gets or sets the period, in milliseconds, at which data frames are polled. 
        /// </summary>
        public int DataPollingPeriod
        {
            get
            {
                return this._dataPollingPeriod;
            }

            private set
            {
                if( value <= 0 ||
                    value > _maxDataPollingPeriod )
                {
                    throw new ArgumentException( "must be > 0 and <= _maxDataPollingPeriod", "DataPollingPeriod" );
                }

                if( value != this._dataPollingPeriod )
                {
                    this._dataPollingPeriod = value;
                    this.NotifyPropertyChanged( "DataPollingPeriod" );
                }
            }
        }

        /// <summary>
        /// Gets a dictionary that maps data channels to corresponding <see cref="ChannelContext"/> objects.
        /// </summary>
        public static Dictionary<EE_DataChannel_t, ChannelContext> ChannelContexts
        {
            get
            {
                return _channelContexts;
            }
        }


        #endregion

        ///////////////////////////////////////////////////////////////////////
        #region Private Properties

        private EmoEngine EmotivEngine
        {
            get
            {
                return this._emotivEngine;
            }
        }

        private BackgroundWorker EmoEngineProcessEventsWorker
        {
            get
            {
                if( this._processEventsWorker == null )
                {
                    InitializeProcessEventsWorker();
                }

                return this._processEventsWorker;
            }
        }

        private BackgroundWorker DataPollingWorker
        {
            get
            {
                if( this._dataPollingWorker == null )
                {
                    this.InitializeDataPollingWorker();
                }

                return this._dataPollingWorker;
            }
        }

        #endregion

        ///////////////////////////////////////////////////////////////////////
        #region Public Methods

        /// <summary>
        /// Starts the engine's <see cref="EmoEngine.ProcessEvents(int)"/> worker thread.
        /// </summary>
        public void StartEmoEngine()
        {
            if( this.CanStartEmoEngine )
            {
                InitializeEmotivEngine();
                this.EmoEngineProcessEventsWorker.RunWorkerAsync();
                this.IsEmoEngineRunning = true;
                NotifyPropertyChanged( "CanStartEmoEngine" );
                NotifyPropertyChanged( "CanStartPolling" );
            }
            else
            {
                throw new InvalidOperationException( "EmoEngine cannot be started" );
            }
        }

        /// <summary>
        /// Starts the engine's <see cref="EmoEngine.GetData"/> worker thread.
        /// </summary>
        public void StartDataPolling()
        {
            if( this.CanStartPolling )
            {
                this.EmotivEngine.DataAcquisitionEnable( this.UserID, true );
                this.SamplingRate = (int)this.EmotivEngine.DataGetSamplingRate( this.UserID );
                //float bufferSizeInSeconds = this.EmotivEngine.EE_DataGetBufferSizeInSec();

                this.DataPollingWorker.RunWorkerAsync();
                this.IsPolling = true;
                NotifyPropertyChanged( "CanStartPolling" );
                NotifyPropertyChanged( "CanStopPolling" );
            }
            else
            {
                throw new InvalidOperationException( "Data polling cannot be started" );
            }
        }

        /// <summary>
        /// Cancels the worker threads.
        /// </summary>
        public void StopDataPolling()
        {
            if( this.CanStopPolling )
            {
                this.DataPollingWorker.CancelAsync();
                this.IsPolling = false;
                NotifyPropertyChanged( "CanStartPolling" );
                NotifyPropertyChanged( "CanStopPolling" );
            }
        }

        #endregion

        ///////////////////////////////////////////////////////////////////////
        #region Construction and Initialization

        /// <summary>
        /// Initializes a new instance of the <see cref="EmoEngineClient"/> class.
        /// </summary>
        public EmoEngineClient()
        {

        }

        /// <summary>
        /// Initializes a new instance of the <see cref="EmoEngineClient"/> class.
        /// </summary>
        static EmoEngineClient()
        {
            PopulateChannelContexts();
        }

        /// <summary>
        /// Defines metadata for each data channel.
        /// </summary>
        private static void PopulateChannelContexts()
        {
            _channelContexts = new Dictionary<EE_DataChannel_t, ChannelContext>();
            _electrodeChannels = new List<EE_DataChannel_t>();

            var channelEnum = Enum.GetValues( typeof( EE_DataChannel_t ) );
            List<EE_DataChannel_t> channels = new List<EE_DataChannel_t>( channelEnum as IEnumerable<EE_DataChannel_t> );

            ChannelContext defaultContext = new ChannelContext();
            defaultContext.Add( "AddToBuffer", false );
            defaultContext.Add( "RemoveDCBias", false );
            defaultContext.Add( "IsElectrodeChannel", false );
            defaultContext.Add( "ComputeFFT", false );

            ChannelContext electrodeContext = new ChannelContext();
            electrodeContext.AddBooleanProperty( "AddToBuffer", true );
            electrodeContext.AddBooleanProperty( "RemoveDCBias", true );
            electrodeContext.AddBooleanProperty( "IsElectrodeChannel", true );
            electrodeContext.AddBooleanProperty( "ComputeFFT", false );
            electrodeContext.Add( "ContactQuality", EE_EEG_ContactQuality_t.EEG_CQ_NO_SIGNAL );

            for( int i = 0; i < channels.Count; i++ )
            {
                switch( channels[i] )
                {
                    case EE_DataChannel_t.AF3:
                    {
                        _channelContexts[channels[i]] = electrodeContext;
                        _channelContexts[channels[i]]["ComputeFFT"] = true;
                        //_channelContexts[channels[i]]["RemoveDCBias"] = false; 
                        _electrodeChannels.Add( channels[i] );

                        break;
                    }
                    case EE_DataChannel_t.AF4:
                    case EE_DataChannel_t.F3:
                    case EE_DataChannel_t.F4:
                    case EE_DataChannel_t.F7:
                    case EE_DataChannel_t.F8:
                    case EE_DataChannel_t.FC5:
                    case EE_DataChannel_t.FC6:
                    case EE_DataChannel_t.O1:
                    case EE_DataChannel_t.O2:
                    case EE_DataChannel_t.P7:
                    case EE_DataChannel_t.P8:
                    case EE_DataChannel_t.T7:
                    case EE_DataChannel_t.T8:
                    {
                        // Handle 
                        _channelContexts[channels[i]] = electrodeContext;
                        _electrodeChannels.Add( channels[i] );
                        break;
                    }

                    case EE_DataChannel_t.COUNTER:
                    {
                        _channelContexts[channels[i]] = defaultContext;
                        _channelContexts[channels[i]]["AddToBuffer"] = true;
                        break;
                    }

                    case EE_DataChannel_t.ES_TIMESTAMP:
                    case EE_DataChannel_t.FUNC_ID:
                    case EE_DataChannel_t.FUNC_VALUE:
                    case EE_DataChannel_t.GYROX:
                    case EE_DataChannel_t.GYROY:
                    case EE_DataChannel_t.INTERPOLATED:
                    case EE_DataChannel_t.MARKER:
                    case EE_DataChannel_t.RAW_CQ:
                    case EE_DataChannel_t.SYNC_SIGNAL:
                    case EE_DataChannel_t.TIMESTAMP:
                    {
                        _channelContexts[channels[i]] = defaultContext;
                        break;
                    }

                    default:
                    {
                        Trace.Assert( false, "Unknown channel enum" );
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Creates the worker thread that polls the engine's <see cref="EmoEngine.ProcessEvents(int)"/> method.
        /// </summary>
        /// <remarks>
        /// The worker thread sleeps for <see cref="EmoEnginePollingPeriod"/> milliseconds and 
        /// calls the engine's <see cref="EmoEngine.ProcessEvents(int)"/> method.
        /// </remarks>
        private void InitializeProcessEventsWorker()
        {
            this._processEventsWorker = new BackgroundWorker();
            this._processEventsWorker.WorkerSupportsCancellation = true;
            this._processEventsWorker.WorkerReportsProgress = true;
            this._processEventsWorker.DoWork += new DoWorkEventHandler( processEventsWorker_DoWork );
            this._processEventsWorker.ProgressChanged += new ProgressChangedEventHandler( processEventsWorker_ProgressChanged );
        }

        /// <summary>
        /// Creates the worker thread that polls the engine's <see cref="EmoEngine.GetData"/> method.
        /// </summary>
        /// <remarks>
        /// The worker thread sleeps for <see cref="DataPollingPeriod"/> milliseconds and 
        /// calls the engine's <see cref="EmoEngine.GetData"/> method.
        /// </remarks>
        private void InitializeDataPollingWorker()
        {
            this._dataPollingWorker = new BackgroundWorker();
            this._dataPollingWorker.WorkerSupportsCancellation = true;
            this._dataPollingWorker.WorkerReportsProgress = true;
            this._dataPollingWorker.DoWork += new DoWorkEventHandler( dataPollingWorker_DoWork );
            this._dataPollingWorker.ProgressChanged += new ProgressChangedEventHandler( dataPollingworker_ProgressChanged );
        }

        /// <summary>
        /// Attaches event handlers for <see cref="EmoEngine"/> events and connects to the neuroheadset. 
        /// </summary>
        private void InitializeEmotivEngine()
        {
            Debug.Assert( this._emotivEngine == null );

            this._emotivEngine = EmoEngine.Instance;
            this._emotivEngine.EmoEngineConnected += new EmoEngine.EmoEngineConnectedEventHandler( engine_EmoEngineConnected );
            this._emotivEngine.InternalStateChanged += new EmoEngine.InternalStateChangedEventHandler( engine_InternalStateChanged );
            this._emotivEngine.EmoStateUpdated += new EmoEngine.EmoStateUpdatedEventHandler( engine_EmoStateUpdated );
            this._emotivEngine.UserAdded += new EmoEngine.UserAddedEventHandler( emotivEngine_UserAdded );

            // Connect to the EmoEngine instance.
            // Use RemoteConnect method for Composer and Control Panel, Connect method for direct-to-headset.
            //this._emotivEngine.RemoteConnect( this.EmoEngineTargetIP, this.ActivePort ); // TBD: GetData fails with this 
            this._emotivEngine.Connect();
        }

        // Disconnects from the neuroheadset.
        private void DisconnectEmotivEngine()
        {
            if( this._emotivEngine != null )
            {
                this._emotivEngine.Disconnect();

                this._emotivEngine = null;
            }
        }

        #endregion

        ///////////////////////////////////////////////////////////////////////
        #region BackgroundWorker event handlers

        void processEventsWorker_DoWork( object sender, DoWorkEventArgs e )
        {
            // Get the BackgroundWorker that raised this event.
            BackgroundWorker worker = sender as BackgroundWorker;

            // Poll the EmoEngine until cancelled by user.
            while( !worker.CancellationPending )
            {
                // TBD: Numeric literal '10000' should be _emotivEngineTimeout, 
                // but a weird exception results from this.
                this.EmotivEngine.ProcessEvents( 10000 );

                Thread.Sleep( this.EmoEnginePollingPeriod );
            }
        }

        // Notifies clients/subscribers that the EmotivState changed.
        void processEventsWorker_ProgressChanged( object sender, ProgressChangedEventArgs pcea )
        {
            this._emotivState = new EmotivState( pcea.UserState as EmoState );

            this.NotifyPropertyChanged( "CurrentEmotivState" );
        }

        void dataPollingWorker_DoWork( object sender, DoWorkEventArgs e )
        {
            // Get the BackgroundWorker that raised this event.
            BackgroundWorker dataPollingworker = sender as BackgroundWorker;

            // Poll the EmoEngine until cancelled by user.
            while( !dataPollingworker.CancellationPending )
            {
                // Block for DataPollingPeriod milliseconds.
                Thread.Sleep( this.DataPollingPeriod );

                Dictionary<EE_DataChannel_t, double[]> currentData;
                lock (this)
                {
                    // Query the neuroheadset for data.
                    currentData = this.EmotivEngine.GetData(this.UserID); ;
                }

                if (currentData != null)
                {
                    //this.CurrentData = new ObservableDataFrame( currentData );

                    // Add the new data frame to the buffer.
                    this.Buffer.AddFrame(currentData);

                    // Notify clients that new a data frame is available. 
                    dataPollingworker.ReportProgress(0);
                }
            }
        }

        void dataPollingworker_ProgressChanged( object sender, ProgressChangedEventArgs pcea )
        {
            this.NotifyPropertyChanged( "Buffer" );
        }

        #endregion

        ///////////////////////////////////////////////////////////////////////
        #region EmoEngine event handlers

        void engine_EmoStateUpdated( object sender, EmoStateUpdatedEventArgs e )
        {
            // ctor clones the EmoState object.
            EmoState emoState = new EmoState( e.emoState );

            // Notifies the client thread that the EmotivState changed.
            this._processEventsWorker.ReportProgress( 0, emoState );
        }

        void engine_InternalStateChanged( object sender, EmoEngineEventArgs e )
        {
            Trace.WriteLine( e.ToString() );
        }

        void engine_EmoEngineConnected( object sender, EmoEngineEventArgs e )
        {
            this.UserID = e.userId;
        }

        void emotivEngine_UserAdded( object sender, EmoEngineEventArgs e )
        {
            this.UserID = e.userId;
        }

        #endregion

        ///////////////////////////////////////////////////////////////////////
        #region INotifyPropertyChanged Members

        /// <summary>
        /// Notifies clients that a property value has changed.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Raises the <see cref="PropertyChanged"/> event.
        /// </summary>
        /// <param name="propertyName"></param>
        protected void NotifyPropertyChanged( String propertyName )
        {
            if( PropertyChanged != null )
            {
                PropertyChanged( this, new PropertyChangedEventArgs( propertyName ) );
            }
        }

        #endregion

        ///////////////////////////////////////////////////////////////////////
        #region IDisposable Members

        /// <summary>
        /// Releases unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose( true );
            GC.SuppressFinalize( this );
        }

        /// <summary>
        /// Releases unmanaged resources.
        /// </summary>
        /// <param name="disposing"><c>true </c>to release both managed and unmanaged 
        /// resources; <c>false</c>to release only unmanaged resources. </param>
        /// <remarks>
        /// The <see cref="DisconnectEmotivEngine"/> method is called. If the 
        /// <see cref="EmoEngine.ProcessEvents(int)"/> and the <see cref="EmoEngine.GetData"/> 
        /// worker threads are running, they are canceled.
        /// </remarks>
        protected virtual void Dispose( bool disposing )
        {
            if( disposing )
            {
                // Free other state (managed objects).
                if( this.CanStopEmoEngine )
                {
                    this._processEventsWorker.CancelAsync();
                }

                if( this.CanStopPolling )
                {
                    this._dataPollingWorker.CancelAsync();
                }

                this.DisconnectEmotivEngine();
            }
        }

        /// <summary>
        /// Finalizer.
        /// </summary>
        ~EmoEngineClient()
        {
            Dispose( false );
        }

        #endregion

        ///////////////////////////////////////////////////////////////////////
        #region Private Fields

        private static List<EE_DataChannel_t> _electrodeChannels;
        private static Dictionary<EE_DataChannel_t, ChannelContext> _channelContexts;

        private const ushort _composerPort = 1726;
        private const ushort _controlPanelPort = 3008;
        private const int _emotivEngineTimeout = 10000;
        private const int _maxSamplingRate = 1000;
        private const int _maxDataPollingPeriod = 1000;
        private const string _targetIP = "127.0.0.1";

        private ushort _activePort = _composerPort;
        private int _emoEnginePollingPeriod = 100;
        private int _dataPollingPeriod = 7; // TBD: Buffer init issues.

        private BackgroundWorker _processEventsWorker;
        private BackgroundWorker _dataPollingWorker;
        private EmoEngine _emotivEngine;
        private EmotivState _emotivState;
        private uint _userId = 0;
        private bool _isPolling;
        private bool _isEmoEngineRunning;
        private int _samplingRate = 128; // TBD: Buffer init issues.
        private SampleBuffer _sampleBuffer;
        private double _bufferSizeFactor = 0.5;
        //private int _fftCounter;
        //private int _fftCounterPeriod = 100;
        //private Dictionary<.EE_DataChannel_t, double[]> _fftDictionary;
        

        #endregion

    }

}
