﻿/*  
 * If you need any help please message us in our discord where us or our community would be hapy to help you out.
 * 
 * If you find a bug please also leave a bug report in the #bug-reports channel of our discord 
 * 
 * (Everything is on the discord)
 * 
 * Discord: https://discord.gg/9AQnUhZRt7
 */
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using DecaSDK.Unity;
using Object = UnityEngine.Object;
using MelonLoader;

namespace DecaSDK
{
    public class DecaMoveBehaviour
    {
        // Used for calibration
        //[Tooltip("Assign this to whatever you want the deca move to match rotations with when you callibrate this, usually your hmd position.")]
        //public Transform head;
        //public Vector3 positionOutput;
        //public Quaternion rotationOutput;
        public Transform OutTransform => outObject.transform;
        private GameObject outObject;
        public Transform HeadTransform;
        private bool Started;
        public bool updatePositon;
        //[Tooltip("This option makes it so that the DecaMove only rotates around the y axis, often this is more usefull")]
        public bool onlyRotateY = true;
        public Vector3 position => _position;
        private Vector3 _position;

        // This is the raw, uncalibrated rotation of the move
        public Quaternion rotation => _rotation;
        private Quaternion _rotation;

        // This is the uncalibrated rotation of the move, but only around a single axis, if you want the callbrated version rotate it by the yOffset variable
        public Quaternion yRotation => Quaternion.AngleAxis(_rotation.eulerAngles.y, Vector3.up);

        // The offset between raw rotation and the calibrated rotation, in radians
        public float yOffset => _yOffset;
        private float _yOffset;
        public float battery => _battery;
        private float _battery;
        public Move.Accuracy accuracy => _accuracy;
        private Move.Accuracy _accuracy;

        // Please note that the button will apear to have some very slight latency, this is because the single/double/triple click calculations are done on the firmware side and it waits to make sure the button isn't going to be pressed agian
        //public UnityEvent onButtonClicked;
        //public UnityEvent onButtonDoubleClicked;
        //public UnityEvent onButtonTrippleClicked;

        // The current state of the connection with the Move: Closed, Open, streaming, etc.
        public Move.State state => _state;
        private Move.State _state;

        // Get if the move is currently asleep
        public bool sleeping => _sleeping;
        private bool _sleeping = true;

        private SharedDisposable<SharedMove> _decaMove;
        // Event callbacks are on another thread so we need to store them until we can process them from the main thread
        private Queue<Move.Feedback> eventQueue = new Queue<Move.Feedback>();
        public String dbOut = "";

        public void Start(){
            try
            {
                Move.OnStateUpdateDelegate OnStateUpdate = (Move.State state) =>
                {
                    lock (this)
                    {
                        _state = state;
                    }
                    Log("State: " + state);
                };
                Move.OnFeedbackDelegate OnFeedback = (feedback) =>
                {
                    // These are called from another thread so we need to use lock to prevent race conditions
                    lock (this)
                    {
                        eventQueue.Enqueue(feedback);
                    }
                    Log("Feedback: " + feedback);
                };
                Move.OnLogMessage OnLogMessage = (Move.LogLevel logLevel, string msg) =>
                {
                    
                    // Debug messages get logged here.
                    Log("DecaLog: \"" + msg + "\" logLevel: " + logLevel);
                };
                Move.OnBatteryUpdateDelegate OnBatteryUpdate = (charge) =>
                {
                    lock (this)
                    {
                        _battery = charge;
                    }
                    Log("Battery charge: " + charge);
                };
                Move.OnOrientationUpdateDelegate OnOrientationUpdate = (quaternion, accuracy, yawCalibration) =>
                {
                    // Update our local variables, again we can't change transform from a different thread so we update them later.
                    lock (this)
                    {
                        _rotation = quaternion.ToUnity();
                        _yOffset = yawCalibration;
                        _accuracy = accuracy;
                    }
                };
                Move.OnImuCalibrationRequestDelegate OnImuCalibrationRequest = () =>
                {
                    Console.WriteLine("Deca Move requested IMU calibration");
                };
                Move.OnPositionUpdateDelegate OnPositionUpdate = (x, y, z) =>
                {
                    // Position from a 3 dof acessory? Yep.
                    lock (this)
                    {
                        _position = new Vector3(x, y, z);
                    }
                };

                _decaMove = SharedMove.Instance;
                _decaMove.Value.AddCallbacks(OnFeedback, OnBatteryUpdate, OnOrientationUpdate, OnPositionUpdate, OnStateUpdate, OnImuCalibrationRequest, OnLogMessage);
                Started = true;
            }
            catch (Exception e)
            {
                LogError("Exception occured in DecaMoveBehaviour: " + e);
            }
        }

        public void Update()
        {
            if(!Started) Start();
            if (!outObject)
            {
                outObject = new GameObject();
            }
            //if(HeadTransform) outObject.transform.parent = HeadTransform;
            if (!onlyRotateY)
                OutTransform.localRotation = Quaternion.AngleAxis(Rad2Deg * _yOffset, Vector3.up) * _rotation;
            else 
                OutTransform.localRotation = Quaternion.AngleAxis(Rad2Deg * _yOffset, Vector3.up) * yRotation;

            if (updatePositon)
                OutTransform.position= Quaternion.AngleAxis(Rad2Deg * _yOffset, Vector3.up) * _position;

            // Execute the events from the event queue
            while (0 < eventQueue.Count)
            {
                Move.Feedback e = eventQueue.Dequeue();
                switch (e)
                {
                    case Move.Feedback.EnteringSleep:
                        // If you want to add your own events here be our guest.
                        _sleeping = true;
                        break;
                    case Move.Feedback.LeavingSleep:
                        _sleeping = false;
                        break;
                    case Move.Feedback.ShuttingDown:
                        _sleeping = true;
                        break;
                    case Move.Feedback.SingleClick:
                        Calibrate();
                        break;
                    case Move.Feedback.DoubleClick:
                        //onButtonDoubleClicked.Invoke();
                        break;
                    case Move.Feedback.TripleClick:
                        //onButtonTrippleClicked.Invoke();
                        break;
                    default:
                        LogError("Unknown Move.Feedback, save your logs and leave angery messages on the Discord.");
                        break;
                }
            }
        }

        public void OnDestroy()
        {
            try
            {
                if(outObject) Object.Destroy(outObject);
                if(_decaMove != null)
                {
                    _decaMove.Dispose();
                    _decaMove = null;
                }
            }
            catch (Exception e)
            {
                Debug.unityLogger.Log(LogType.Error, "Exception occured in DecaMoveBehaviour OnDestroy: " + e);
            }
        }

        public void SendHaptic()
        { 
            try
            {
                _decaMove.Value.SendHaptic();
            }
            catch (DecaSDK.Move.NativeCallFailedException e)
            {
                if (e.Status != DecaSDK.Move.Status.ErrorNotConnected)
                {
                    throw;
                }
            }
        }
        public void Calibrate()
        {
            if (!HeadTransform) LogError($"calibate Failed, no head");
            try
            {
                Quaternion parentRotationOffset = Quaternion.identity;
                    if(outObject.transform.parent) parentRotationOffset = Quaternion.Inverse(outObject.transform.parent.rotation);
                    //Vector3 headForward = HeadTransform.rotation.eulerAngles; 
                    Vector3 headForward = parentRotationOffset * HeadTransform.forward;
                    
                _decaMove.Value.Calibrate(headForward.z, headForward.x);
                LogError($"calibated {headForward.ToString()}");
            }
            catch (DecaSDK.Move.NativeCallFailedException e)
            {
                if (e.Status != DecaSDK.Move.Status.ErrorNotConnected)
                {
                    throw;
                }
            }
        }

        void Log(String message)
        {
            //Debug.Log($"[DecaSDK] {message}");
            //dbOut = message;
        }void LogError(String message)
        {
            //Debug.LogError($"[DecaSDK] {message}");
            dbOut =$"ERROR {message}" ;
        }
        const float Rad2Deg = 57.29578f;
    }
}
