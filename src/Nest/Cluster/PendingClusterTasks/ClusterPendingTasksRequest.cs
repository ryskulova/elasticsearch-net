﻿using Elasticsearch.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nest
{
	public interface IClusterPendingTasksRequest : IRequest<ClusterPendingTasksRequestParameters> { }

	public partial class ClusterPendingTasksRequest 
		: BasePathRequest<ClusterPendingTasksRequestParameters>, IClusterPendingTasksRequest { }

	public partial class ClusterPendingTasksDescriptor 
		: BasePathDescriptor<ClusterPendingTasksDescriptor, ClusterPendingTasksRequestParameters>, IClusterPendingTasksRequest { }
}
