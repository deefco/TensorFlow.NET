﻿/*****************************************************************************
   Copyright 2018 The TensorFlow.NET Authors. All Rights Reserved.

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
******************************************************************************/

using NumSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using NumSharp.Backends;
using NumSharp.Backends.Unmanaged;
using NumSharp.Utilities;
using Tensorflow.Framework;
using static Tensorflow.Binding;

namespace Tensorflow
{
    /// <summary>
    /// A tensor is a generalization of vectors and matrices to potentially higher dimensions. 
    /// Internally, TensorFlow represents tensors as n-dimensional arrays of base datatypes.
    /// </summary>
    [SuppressMessage("ReSharper", "ConvertToAutoProperty")]
    public partial class Tensor : DisposableObject, ITensorOrOperation, _TensorLike
    {
        private readonly int _id;
        private readonly Operation _op;
        private readonly int _value_index;
        private TF_Output? _tf_output;
        private readonly TF_DataType _override_dtype;

        public int Id => _id;

        /// <summary>
        ///     The Graph that contains this tensor.
        /// </summary>
        public Graph graph => op?.graph;

        /// <summary>
        ///     The Operation that produces this tensor as an output.
        /// </summary>
        public Operation op => _op;

        public Tensor[] outputs => op.outputs;

        /// <summary>
        ///     The string name of this tensor.
        /// </summary>
        public string name => $"{(op == null ? "<unnamed Operation>" : $"{op.name}:{_value_index}")}";

        /// <summary>
        ///     The index of this tensor in the outputs of its Operation.
        /// </summary>
        public int value_index => _value_index;

        /// <summary>
        ///     The DType of elements in this tensor.
        /// </summary>
        public TF_DataType dtype => _handle == IntPtr.Zero ? _override_dtype : c_api.TF_TensorType(_handle);

        public ulong bytesize => _handle == IntPtr.Zero ? 0 : c_api.TF_TensorByteSize(_handle);
        public ulong itemsize => _handle == IntPtr.Zero ? 0 : c_api.TF_DataTypeSize(dtype);
        public ulong size => _handle == IntPtr.Zero ? 0 : bytesize / itemsize;
        public IntPtr buffer => _handle == IntPtr.Zero ? IntPtr.Zero : c_api.TF_TensorData(_handle);
        public int num_consumers(TF_Output oper_out) => _handle == IntPtr.Zero ? 0 : c_api.TF_OperationOutputNumConsumers(oper_out);
        public int NDims => rank;

        /// <summary>
        ///     The name of the device on which this tensor will be produced, or null.
        /// </summary>
        public string Device => op.Device;

        public int[] dims => shape;

        /// <summary>
        ///     Used for keep other pointer when do implicit operating
        /// </summary>
        public object Tag { get; set; }


        /// <summary>
        ///     Returns the shape of a tensor.
        /// </summary>
        /// <remarks>https://www.tensorflow.org/api_docs/python/tf/shape</remarks>
        public int[] shape
        {
            get
            {
                var dims = new long[rank < 0 ? 0 : rank];

                if (_handle == IntPtr.Zero)
                {
                    var status = new Status();
                    c_api.TF_GraphGetTensorShape(op.graph, _as_tf_output(), dims, rank, status);
                    status.Check();
                } else
                {
                    for (int i = 0; i < rank; i++)
                        dims[i] = c_api.TF_Dim(_handle, i);
                }

                return dims.Select(x => ((IConvertible) x).ToInt32(CultureInfo.InvariantCulture)).ToArray();
            }

            set
            {
                var status = new Status();

                if (value == null)
                    c_api.TF_GraphSetTensorShape(this.graph, this._as_tf_output(), null, -1, status);
                else
                    c_api.TF_GraphSetTensorShape(this.graph, this._as_tf_output(), value.Select(Convert.ToInt64).ToArray(), value.Length, status);
            }
        }

        public int[] _shape_tuple()
        {
            return (int[]) shape.Clone();
        }

        public TensorShape TensorShape => tensor_util.to_shape(shape);

        /// <summary>
        ///     Updates the shape of this tensor.
        /// </summary>
        public void set_shape(TensorShape shape) 
        {
            this.shape = (int[]) shape.dims.Clone();
        }

        /// <summary>
        ///     Updates the shape of this tensor.
        /// </summary>
        [Obsolete("Please use set_shape(TensorShape shape) instead.", false)]
        public void SetShape(TensorShape shape) 
        {
            this.shape = (int[]) shape.dims.Clone();
        }

        /// <summary>
        ///     Updates the shape of this tensor.
        /// </summary>
        public void set_shape(Tensor shape)
        {
            // ReSharper disable once MergeConditionalExpression
            this.shape = shape is null ? null : shape.shape;
        }

        /// <summary>
        /// number of dimensions <br></br>
        /// 0	Scalar (magnitude only) <br></br>
        /// 1	Vector (magnitude and direction) <br></br>
        /// 2	Matrix (table of numbers) <br></br>
        /// 3	3-Tensor (cube of numbers) <br></br>
        /// n	n-Tensor (you get the idea)
        /// </summary>
        /// <remarks>https://www.tensorflow.org/api_docs/python/tf/rank</remarks>
        public int rank
        {
            get
            {
                if (_handle == IntPtr.Zero)
                {
                    var status = new Status();
                    var output = _as_tf_output();
                    int ndim = c_api.TF_GraphGetTensorNumDims(op.graph, output, status);
                    status.Check();
                    return ndim;
                }

                return c_api.TF_NumDims(_handle);
            }
        }

        /// <summary>
        ///     Returns a list of Operations that consume this tensor.
        /// </summary>
        /// <returns></returns>
        public Operation[] consumers()
        {
            var output = _as_tf_output();
            var consumer_names = c_api.TF_OperationOutputConsumers_wrapper(output);
            return consumer_names.Select(x => graph.OperationByName(x)).ToArray();
        }

        public TF_Output _as_tf_output()
        {
            if (!_tf_output.HasValue)
                _tf_output = new TF_Output(op, value_index);

            return _tf_output.Value;
        }

        [Obsolete("Please use ToArray<T>() instead.", false)]
        public T[] Data<T>() where T : unmanaged
        {
            return ToArray<T>();
        }

        /// <summary>
        ///     
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        /// <exception cref="ArgumentException">When <typeparam name="T"> is string </typeparam></exception>
        public T[] ToArray<T>() where T : unmanaged
        {
            //when T is string
            if (typeof(T) == typeof(string))
            {
                if (dtype != TF_DataType.TF_STRING)
                    throw new ArgumentException($"Given <{typeof(T).Name}> can't be converted to string.");

                return (T[]) (object) StringData();
            }

            //Are the types matching?
            if (typeof(T).as_dtype() == dtype)
            {
                if (NDims == 0 && size == 1)  //is it a scalar?
                {
                    unsafe
                    {
                        return new T[] {*(T*) buffer};
                    }
                }

                //types match, no need to perform cast
                var ret = new T[size];
                unsafe
                {
                    var len = (long) size;
                    fixed (T* dstRet = ret)
                    {
                        T* dst = dstRet; //local stack copy
                        if (typeof(T).IsPrimitive)
                        {
                            var src = (T*) buffer;
                            len *= ((long) itemsize);
                            System.Buffer.MemoryCopy(src, dst, len, len);
                        } else
                        {
                            var itemsize = (long) this.itemsize;
                            var buffer = this.buffer.ToInt64();
                            Parallel.For(0L, len, i => dst[i] = Marshal.PtrToStructure<T>(new IntPtr(buffer + i * itemsize)));
                        }
                    }
                }

                return ret;
            } else
            {
                
                //types do not match, need to perform cast
                if (NDims == 0 && size == 1) //is it a scalar?
                {
                    unsafe
                    {
#if _REGEN
		                #region Compute
		                switch (dtype.as_numpy_dtype().GetTypeCode())
		                {
			                %foreach supported_dtypes,supported_dtypes_lowercase%
			                case NPTypeCode.#1: return new T[] {Converts.ChangeType<T>(*(#2*) buffer, NPTypeCode.#1)};
			                %
			                case NPTypeCode.String: return new T[] {Converts.ChangeType<T>((string)this, NPTypeCode.String)};
			                default:
				                throw new NotSupportedException();
		                }
		                #endregion
#else
		                #region Compute
		                switch (dtype.as_numpy_dtype()?.GetTypeCode())
		                {
			                case NPTypeCode.Boolean: return new T[] {Converts.ChangeType<T>(*(bool*) buffer, NPTypeCode.Boolean)};
			                case NPTypeCode.Byte: return new T[] {Converts.ChangeType<T>(*(byte*) buffer, NPTypeCode.Byte)};
			                case NPTypeCode.Int16: return new T[] {Converts.ChangeType<T>(*(short*) buffer, NPTypeCode.Int16)};
			                case NPTypeCode.UInt16: return new T[] {Converts.ChangeType<T>(*(ushort*) buffer, NPTypeCode.UInt16)};
			                case NPTypeCode.Int32: return new T[] {Converts.ChangeType<T>(*(int*) buffer, NPTypeCode.Int32)};
			                case NPTypeCode.UInt32: return new T[] {Converts.ChangeType<T>(*(uint*) buffer, NPTypeCode.UInt32)};
			                case NPTypeCode.Int64: return new T[] {Converts.ChangeType<T>(*(long*) buffer, NPTypeCode.Int64)};
			                case NPTypeCode.UInt64: return new T[] {Converts.ChangeType<T>(*(ulong*) buffer, NPTypeCode.UInt64)};
			                case NPTypeCode.Char: return new T[] {Converts.ChangeType<T>(*(char*) buffer, NPTypeCode.Char)};
			                case NPTypeCode.Double: return new T[] {Converts.ChangeType<T>(*(double*) buffer, NPTypeCode.Double)};
			                case NPTypeCode.Single: return new T[] {Converts.ChangeType<T>(*(float*) buffer, NPTypeCode.Single)};
			                case NPTypeCode.String: return new T[] {Converts.ChangeType<T>((string)this, NPTypeCode.String)};
			                default:
                                throw new NotSupportedException();
		                }
		                #endregion
#endif
                    }
                }

                var ret = new T[size];
                unsafe
                {
                    var len = (long) size;
                    fixed (T* dstRet = ret)
                    {
                        T* dst = dstRet; //local stack copy

#if _REGEN
		                #region Compute
		                switch (dtype.as_numpy_dtype().GetTypeCode())
		                {
			                %foreach supported_dtypes,supported_dtypes_lowercase%
			                case NPTypeCode.#1: new UnmanagedMemoryBlock<#2>((#2*) buffer, len).CastTo(new UnmanagedMemoryBlock<T>(dst, len), null, null); break;
			                %
			                default:
				                throw new NotSupportedException();
		                }
		                #endregion
#else
		                #region Compute
		                switch (dtype.as_numpy_dtype().GetTypeCode())
		                {
			                case NPTypeCode.Boolean: new UnmanagedMemoryBlock<bool>((bool*) buffer, len).CastTo(new UnmanagedMemoryBlock<T>(dst, len), null, null); break;
			                case NPTypeCode.Byte: new UnmanagedMemoryBlock<byte>((byte*) buffer, len).CastTo(new UnmanagedMemoryBlock<T>(dst, len), null, null); break;
			                case NPTypeCode.Int16: new UnmanagedMemoryBlock<short>((short*) buffer, len).CastTo(new UnmanagedMemoryBlock<T>(dst, len), null, null); break;
			                case NPTypeCode.UInt16: new UnmanagedMemoryBlock<ushort>((ushort*) buffer, len).CastTo(new UnmanagedMemoryBlock<T>(dst, len), null, null); break;
			                case NPTypeCode.Int32: new UnmanagedMemoryBlock<int>((int*) buffer, len).CastTo(new UnmanagedMemoryBlock<T>(dst, len), null, null); break;
			                case NPTypeCode.UInt32: new UnmanagedMemoryBlock<uint>((uint*) buffer, len).CastTo(new UnmanagedMemoryBlock<T>(dst, len), null, null); break;
			                case NPTypeCode.Int64: new UnmanagedMemoryBlock<long>((long*) buffer, len).CastTo(new UnmanagedMemoryBlock<T>(dst, len), null, null); break;
			                case NPTypeCode.UInt64: new UnmanagedMemoryBlock<ulong>((ulong*) buffer, len).CastTo(new UnmanagedMemoryBlock<T>(dst, len), null, null); break;
			                case NPTypeCode.Char: new UnmanagedMemoryBlock<char>((char*) buffer, len).CastTo(new UnmanagedMemoryBlock<T>(dst, len), null, null); break;
			                case NPTypeCode.Double: new UnmanagedMemoryBlock<double>((double*) buffer, len).CastTo(new UnmanagedMemoryBlock<T>(dst, len), null, null); break;
			                case NPTypeCode.Single: new UnmanagedMemoryBlock<float>((float*) buffer, len).CastTo(new UnmanagedMemoryBlock<T>(dst, len), null, null); break;
			                case NPTypeCode.String: throw new NotSupportedException("Unable to convert from string to other dtypes"); //TODO! this should call Converts.To<T> 
			                default:
				                throw new NotSupportedException();
		                }
		                #endregion
#endif
                        
                    }
                }
                
                return ret;
            }
        }

        /// <summary>
        ///     Copies the memory of current buffer onto newly allocated array.
        /// </summary>
        /// <returns></returns>
        [Obsolete("Please use set_shape(TensorShape shape) instead.", false)]
        public byte[] Data()
        {
            return BufferToArray();
        }

        /// <summary>
        ///     Copies the memory of current buffer onto newly allocated array.
        /// </summary>
        /// <returns></returns>
        public byte[] BufferToArray()
        {
            unsafe
            {
                // ReSharper disable once LocalVariableHidesMember
                var bytesize = (long) this.bytesize;
                var data = new byte[bytesize];
                fixed (byte* dst = data) 
                    System.Buffer.MemoryCopy(buffer.ToPointer(), dst, bytesize, bytesize);

                return data;
            }
        }

        /// Used internally in ToArray&lt;T&gt;
        private unsafe string[] StringData()
        {
            //
            // TF_STRING tensors are encoded with a table of 8-byte offsets followed by TF_StringEncode-encoded bytes.
            // [offset1, offset2,...,offsetn, s1size, s1bytes, s2size, s2bytes,...,snsize,snbytes]
            //
            long size = 1;
            foreach (var s in TensorShape.dims)
                size *= s;

            var buffer = new byte[size][];
            var src = c_api.TF_TensorData(_handle);
            var srcLen = (IntPtr) (src.ToInt64() + (long) bytesize);
            src += (int) (size * 8);
            for (int i = 0; i < buffer.Length; i++)
            {
                using (var status = new Status())
                {
                    IntPtr dst = IntPtr.Zero;
                    UIntPtr dstLen = UIntPtr.Zero;
                    var read = c_api.TF_StringDecode((byte*) src, (UIntPtr) (srcLen.ToInt64() - src.ToInt64()), (byte**) &dst, &dstLen, status);
                    status.Check(true);
                    buffer[i] = new byte[(int) dstLen];
                    Marshal.Copy(dst, buffer[i], 0, buffer[i].Length);
                    src += (int) read;
                }
            }

            var _str = new string[buffer.Length];
            for (int i = 0; i < _str.Length; i++)
                _str[i] = Encoding.UTF8.GetString(buffer[i]);

            return _str;
        }

        public Tensor MaybeMove()
        {
            var tensor = c_api.TF_TensorMaybeMove(_handle);
            return tensor;
        }

        /// <summary>
        ///     Evaluates this tensor in a `Session`.
        /// </summary>
        /// <param name="feed_dict">A dictionary that maps `Tensor` objects to feed values.</param>
        /// <returns>A <see cref="NumSharp"/> array corresponding to the value of this tensor.</returns>
        public NDArray eval(params FeedItem[] feed_dict)
        {
            return ops._eval_using_default_session(this, feed_dict, graph);
        }

        /// <summary>
        ///     Evaluates this tensor in a `Session`.
        /// </summary>
        /// <param name="feed_dict">A dictionary that maps `Tensor` objects to feed values.</param>
        /// <param name="session">The `Session` to be used to evaluate this tensor.</param>
        /// <returns>A <see cref="NumSharp"/> array corresponding to the value of this tensor.</returns>
        public NDArray eval(Session session, FeedItem[] feed_dict = null)
        {
            return ops._eval_using_default_session(this, feed_dict, graph, session);
        }

        public Tensor slice(Slice slice)
        {
            var slice_spec = new int[] {slice.Start.Value};
            var begin = new List<int>();
            var end = new List<int>();
            var strides = new List<int>();

            var index = 0;
            var (new_axis_mask, shrink_axis_mask) = (0, 0);
            var (begin_mask, end_mask) = (0, 0);
            var ellipsis_mask = 0;

            foreach (var s in slice_spec)
            {
                begin.Add(s);
                if (slice.Stop.HasValue)
                {
                    end.Add(slice.Stop.Value);
                } else
                {
                    end.Add(0);
                    end_mask |= (1 << index);
                }

                strides.Add(slice.Step);

                index += 1;
            }

            return tf_with(ops.name_scope(null, "strided_slice", new {begin, end, strides}), scope =>
            {
                string name = scope;
                if (begin != null)
                {
                    var (packed_begin, packed_end, packed_strides) =
                        (array_ops.stack(begin.ToArray()),
                            array_ops.stack(end.ToArray()),
                            array_ops.stack(strides.ToArray()));

                    return gen_array_ops.strided_slice(
                        this,
                        packed_begin,
                        packed_end,
                        packed_strides,
                        begin_mask: begin_mask,
                        end_mask: end_mask,
                        shrink_axis_mask: shrink_axis_mask,
                        new_axis_mask: new_axis_mask,
                        ellipsis_mask: ellipsis_mask,
                        name: name);
                }

                throw new NotImplementedException("");
            });
        }

        public Tensor slice(int start)
        {
            var slice_spec = new int[] {start};
            var begin = new List<int>();
            var end = new List<int>();
            var strides = new List<int>();

            var index = 0;
            var (new_axis_mask, shrink_axis_mask) = (0, 0);
            var (begin_mask, end_mask) = (0, 0);
            var ellipsis_mask = 0;

            foreach (var s in slice_spec)
            {
                begin.Add(s);
                end.Add(s + 1);
                strides.Add(1);
                shrink_axis_mask |= (1 << index);
                index += 1;
            }

            return tf_with(ops.name_scope(null, "strided_slice", new {begin, end, strides}), scope =>
            {
                string name = scope;
                if (begin != null)
                {
                    var (packed_begin, packed_end, packed_strides) =
                        (array_ops.stack(begin.ToArray()),
                            array_ops.stack(end.ToArray()),
                            array_ops.stack(strides.ToArray()));

                    return gen_array_ops.strided_slice(
                        this,
                        packed_begin,
                        packed_end,
                        packed_strides,
                        begin_mask: begin_mask,
                        end_mask: end_mask,
                        shrink_axis_mask: shrink_axis_mask,
                        new_axis_mask: new_axis_mask,
                        ellipsis_mask: ellipsis_mask,
                        name: name);
                }

                throw new NotImplementedException("");
            });
        }

        public override string ToString()
        {
            // this can throw IndexOutOfRangeException 
            //if(NDims == 0)
            //{
            //    switch (dtype)
            //    {
            //        case TF_DataType.TF_INT32:
            //            return Data<int>()[0].ToString();
            //    }
            //}

            return $"tf.Tensor '{name}' shape=({string.Join(",", shape)}) dtype={dtype}";
        }

        protected override void DisposeUnmanagedResources(IntPtr handle)
        {
            if (handle != IntPtr.Zero)
            {
                c_api.TF_DeleteTensor(handle);
                _handle = IntPtr.Zero;
            }
        }

        public bool IsDisposed
        {
            get
            {
                lock (this)
                {
                    return _handle == IntPtr.Zero;
                }
            }
        }

        public int tensor_int_val { get; set; }
    }
}