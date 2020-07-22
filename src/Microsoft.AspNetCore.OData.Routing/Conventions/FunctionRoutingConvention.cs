﻿// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.OData.Abstracts;
using Microsoft.AspNetCore.OData.Routing.Edm;
using Microsoft.AspNetCore.OData.Routing.Template;
using Microsoft.OData.Edm;

namespace Microsoft.AspNetCore.OData.Routing.Conventions
{
    /// <summary>
    /// The convention for <see cref="IEdmFunction"/>.
    /// Get ~/entity|singleton/function,  ~/entity|singleton/cast/function
    /// Get ~/entity|singleton/key/function, ~/entity|singleton/key/cast/function
    /// </summary>
    public class FunctionRoutingConvention : IODataControllerActionConvention
    {
        /// <inheritdoc />
        public int Order => 700;

        /// <inheritdoc />
        public virtual bool AppliesToController(ODataControllerActionContext context)
        {
            // bound operation supports for entity set and singleton
            return context?.EntitySet != null || context?.Singleton != null;
        }

        /// <inheritdoc />
        public virtual bool AppliesToAction(ODataControllerActionContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            IEdmNavigationSource navigationSource = context.EntitySet == null ?
                (IEdmNavigationSource)context.Singleton :
                (IEdmNavigationSource)context.EntitySet;

            IEdmModel model = context.Model;
            string prefix = context.Prefix;
            IEdmEntityType entityType = navigationSource.EntityType();

            ActionModel action = context.Action;

            // function should have the [HttpGet]
            if (!action.Attributes.Any(a => a is HttpGetAttribute))
            {
                return false;
            }

            bool hasKeyParameter = action.HasODataKeyParameter(entityType);
            if (context.Singleton != null && hasKeyParameter)
            {
                // Singleton, doesn't allow to query property with key
                // entityset, doesn't allow for non-key to query property
                return false;
            }

            string actionName = action.ActionMethod.Name;
            IEnumerable<IEdmFunction> candidates = model.SchemaElements.OfType<IEdmFunction>().Where(f => f.IsBound && f.Name == actionName);
            foreach (IEdmFunction edmFunction in candidates)
            {
                IEdmOperationParameter bindingParameter = edmFunction.Parameters.FirstOrDefault();
                if (bindingParameter == null)
                {
                    continue;
                }

                IEdmTypeReference bindingType = bindingParameter.Type;
                bool bindToCollection = bindingType.TypeKind() == EdmTypeKind.Collection;
                if (bindToCollection)
                {
                    // if binding to collection and the action has key parameter or a singleton, skip
                    if (context.Singleton != null || hasKeyParameter)
                    {
                        continue;
                    }
                }
                else
                {
                    // if binding to non-collection and the action hasn't key parameter, skip
                    if (context.EntitySet != null && !hasKeyParameter)
                    {
                        continue;
                    }
                }

                if (!bindingType.Definition.IsEntityOrEntityCollectionType(out IEdmEntityType bindingEntityType))
                {
                    continue;
                }

                IEdmEntityType castType = null;
                if (entityType.IsOrInheritsFrom(bindingEntityType))
                {
                    // True if and only if the thisType is equivalent to or inherits from otherType.
                    castType = null;
                }
                else if (bindingEntityType.InheritsFrom(entityType))
                {
                    // True if and only if the type inherits from the potential base type.
                    castType = bindingEntityType;
                }
                else
                {
                    continue;
                }

                // TODO: need discussion ahout:
                // 1) Do we need to match the whole parameter count?
                // 2) Do we need to select the best match? So far, i don't think and let it go.
                if (!IsFunctionParameterMeet(edmFunction, action))
                {
                    continue;
                }

                // Now, let's add the selector model.
                IList<ODataSegmentTemplate> segments = new List<ODataSegmentTemplate>();
                if (context.EntitySet != null)
                {
                    segments.Add(new EntitySetSegmentTemplate(context.EntitySet));
                    if (hasKeyParameter)
                    {
                        segments.Add(new KeySegmentTemplate(entityType, navigationSource));
                    }
                }
                else
                {
                    segments.Add(new SingletonSegmentTemplate(context.Singleton));
                }

                if (castType != null)
                {
                    if (context.Singleton != null || !hasKeyParameter)
                    {
                        segments.Add(new CastSegmentTemplate(castType, entityType, navigationSource));
                    }
                    else
                    {
                        segments.Add(new CastSegmentTemplate(new EdmCollectionType(castType.ToEdmTypeReference(false)),
                            new EdmCollectionType(entityType.ToEdmTypeReference(false)), navigationSource));
                    }
                }

                IEdmNavigationSource targetset = edmFunction.GetTargetEntitySet(navigationSource, context.Model);

                segments.Add(new FunctionSegmentTemplate(edmFunction, targetset));
                ODataPathTemplate template = new ODataPathTemplate(segments);
                action.AddSelector(prefix, model, template);
            }

            // in OData operationImport routing convention, all action are processed by default
            // even it's not a really edm operation import call.
            return false;
        }

        private static bool IsFunctionParameterMeet(IEdmFunction function, ActionModel action)
        {
            // we can allow the action has other parameters except the functio parameters.
            foreach (var parameter in function.Parameters.Skip(1))
            {
                // It seems we don't need to distinguish the optional parameter here
                // It means whether it's optional parameter or not, the action descriptor should have such parameter defined.
                // Meanwhile, the send request may or may not have such parameter value.
                //IEdmOptionalParameter optionalParameter = parameter as IEdmOptionalParameter;
                //if (optionalParameter != null)
                //{
                //    continue;
                //}
                if (!action.Parameters.Any(p => p.ParameterInfo.Name == parameter.Name))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
